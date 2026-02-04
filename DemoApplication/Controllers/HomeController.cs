using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Azure.Storage;
using Azure.Storage.Sas;
using System.Text.Json;
using System.Text;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<HomeController> _logger;

    public HomeController(AppDbContext db, IConfiguration config, ILogger<HomeController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }
    [HttpPost]
    public async Task<IActionResult> OnPostCallFunction()
    {
        string orderId = string.Empty;
        string status = string.Empty;

        try
        {
            // ✅ Prefer form data if present (backward compatibility)
            if (Request.HasFormContentType)
            {
                orderId = Request.Form["orderId"];
                status = Request.Form["status"];
            }
            else
            {
                // ✅ Async read body to avoid InvalidOperationException
                using var sr = new StreamReader(Request.Body);
                var body = await sr.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("orderId", out var o))
                            orderId = o.GetString() ?? string.Empty;

                        if (root.TryGetProperty("status", out var s))
                            status = s.GetString() ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to parse JSON body for OnPostCallFunction");
                    }
                }
            }

            // 🔍 Optional validation
            if (string.IsNullOrWhiteSpace(orderId))
                return BadRequest("orderId is required");

            // 🔗 Azure Function URL from config
            var functionUrl = _config["AzureFunctionUrl"];

            using var client = new HttpClient();

            var payload = new
            {
                orderId,
                status
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await client.PostAsync(functionUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            return Ok(new
            {
                success = response.IsSuccessStatusCode,
                response = responseBody
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnPostCallFunction failed");
            return StatusCode(500, ex.Message);
        }
    }
    // Returns a short-lived SAS URL for a private blob
    [HttpGet]
    public IActionResult OnGetImageSas(string blobName)
    {
        try
        {
            var accountName = _config["StorageAccountName"];
            var accountKey = _config["StorageAccountKey"];
            var container = _config["BlobContainer"];

            if (string.IsNullOrWhiteSpace(accountName) ||
                string.IsNullOrWhiteSpace(accountKey) ||
                string.IsNullOrWhiteSpace(container))
            {
                return Json(new { error = "StorageAccountName, StorageAccountKey or BlobContainer not configured." });
            }

            if (string.IsNullOrWhiteSpace(blobName))
            {
                blobName = _config["DefaultBlobName"] ?? "image.png";
            }

            var blobUri = new Uri(
                $"https://{accountName}.blob.core.windows.net/{container}/{Uri.EscapeDataString(blobName)}"
            );

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = container,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(10) // short-lived
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var credential = new StorageSharedKeyCredential(accountName, accountKey);
            var sasQuery = sasBuilder.ToSasQueryParameters(credential).ToString();

            var uriWithSas = new UriBuilder(blobUri)
            {
                Query = sasQuery
            };

            return Json(new { url = uriWithSas.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnGetImageSas failed");
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> LoadUsers()
    {
        try
        {
            var users = await _db.Users.ToListAsync();
            return Json(users);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "DB connection failed", details = ex.Message });
        }
    }
}
