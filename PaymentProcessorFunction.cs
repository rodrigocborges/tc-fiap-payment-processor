using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace PaymentProcessor;

public enum PaymentStatus
{
    Pending = 0, Checking = 1, Approved = 2, Canceled = 3, Repproved = 4
}

public class GetPaymentResponse
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid UserId { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateStatusPaymentRequest
{
    public PaymentStatus Status { get; set; }
}

public class PaymentProcessorFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PaymentProcessorFunction> _logger;
    private static readonly Random _random = new Random();

    public PaymentProcessorFunction(IHttpClientFactory httpClientFactory, ILogger<PaymentProcessorFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // "0 */1 * * * *" significa "no segundo 0 de cada minuto", ou seja, a cada minuto.
    [Function("PaymentProcessorFunction")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo myTimer)
    {
        _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
        
        var apiUrl = Environment.GetEnvironmentVariable("API_URL");
        var apiKey = Environment.GetEnvironmentVariable("API_KEY");

        if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogError("As Application Settings API_URL e API_KEY não estão configuradas.");
            return;
        }

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);

        try
        {
            // 2. Obter a lista de pagamentos pendentes
            _logger.LogInformation($"A obter pagamentos pendentes de {apiUrl}/payments/pending");
            var pendingPayments = await httpClient.GetFromJsonAsync<List<GetPaymentResponse>>($"{apiUrl}/payments/pending");

            if (pendingPayments == null || !pendingPayments.Any())
            {
                _logger.LogInformation("Nenhum pagamento pendente encontrado.");
                return;
            }

            _logger.LogInformation($"{pendingPayments.Count} pagamentos pendentes encontrados. A iniciar atualização...");

            // 3. Iterar e atualizar cada pagamento
            foreach (var payment in pendingPayments)
            {
                var newStatus = GetRandomStatus();
                var updatePayload = new UpdateStatusPaymentRequest { Status = newStatus };
                
                _logger.LogInformation($"A atualizar pagamento {payment.Id} para o status {newStatus}");
                
                var patchResponse = await httpClient.PatchAsJsonAsync($"{apiUrl}/payments/{payment.Id}", updatePayload);

                if (patchResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Pagamento {payment.Id} atualizado com sucesso.");
                }
                else
                {
                    _logger.LogError($"Falha ao atualizar o pagamento {payment.Id}. Status: {patchResponse.StatusCode}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Ocorreu um erro inesperado: {ex.Message}");
        }

        _logger.LogInformation("Processador de pagamentos finalizado.");
    }

    private PaymentStatus GetRandomStatus()
    {
        var possibleStatuses = new[] 
        { 
            PaymentStatus.Checking, 
            PaymentStatus.Approved, 
            PaymentStatus.Canceled, 
            PaymentStatus.Repproved 
        };
        int index = _random.Next(possibleStatuses.Length);
        return possibleStatuses[index];
    }
}