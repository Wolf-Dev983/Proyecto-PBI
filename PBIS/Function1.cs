using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

public class PBIRequest
{
    public required string Title { get; set; }
    public required string State { get; set; }
    public required string Description { get; set; }
    public required string Priority { get; set; }
    public int Effort { get; set; }
}

public static class CreatePBI
{
    [Function("CreatePBI")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req,
        FunctionContext executionContext)
    {
        var log = executionContext.GetLogger("CreatePBI");
        log.LogInformation("C# HTTP trigger function processed a request.");

        // Leer el cuerpo de la solicitud
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        log.LogInformation($"Cuerpo de la solicitud: {requestBody}"); // Registro del cuerpo

        PBIRequest data;
        try
        {
            data = JsonConvert.DeserializeObject<PBIRequest>(requestBody);
        }
        catch (JsonException ex)
        {
            log.LogError($"Error deserializando JSON: {ex.Message}");
            return new BadRequestObjectResult("Error en el formato JSON. Detalle: " + ex.Message);
        }

        // Validar los datos deserializados
        if (data == null || string.IsNullOrWhiteSpace(data.Title) || string.IsNullOrWhiteSpace(data.Description))
        {
            return new BadRequestObjectResult("Los campos 'Title' y 'Description' son obligatorios.");
        }

        // Convertir prioridad a valor
        int priorityValue = data.Priority.ToLower() switch
        {
            "alta" => 2,
            "media" => 1,
            "baja" => 0,
            _ => -1 // Valor no válido
        };

        if (priorityValue == -1)
        {
            return new BadRequestObjectResult("Prioridad no válida. Debe ser 'alta', 'media' o 'baja'.");
        }

        // Lógica para crear el PBI en Azure DevOps
        string pat = Environment.GetEnvironmentVariable("AZURE_DEVOPS_PAT");
        if (string.IsNullOrEmpty(pat))
        {
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}")));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json-patch+json"));

        var jsonBody = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = data.Title },
            new { op = "add", path = "/fields/System.State", value = data.State},
            new { op = "add", path = "/fields/System.Description", value = data.Description },
            new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = priorityValue },
            new { op = "add", path = "/fields/Custom.Effort", value = data.Effort }
        };

        var content = new StringContent(JsonConvert.SerializeObject(jsonBody), Encoding.UTF8, "application/json-patch+json");
        var response = await httpClient.PostAsync("https://dev.azure.com/dbaron49/Pruebas/_apis/wit/workitems/$Product%20Backlog%20Item?api-version=6.0", content);

        if (response.IsSuccessStatusCode)
        {
            return new OkObjectResult("PBI creado exitosamente.");
        }
        else
        {
            log.LogError($"Error al crear PBI en Azure DevOps: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
            return new StatusCodeResult((int)response.StatusCode);
        }
    }
}