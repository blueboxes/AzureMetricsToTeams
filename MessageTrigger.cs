using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Reflection;
using AdaptiveCards.Templating;
using Microsoft.Extensions.Configuration;
using Azure.ResourceManager;
using Azure.Identity;
using Azure.ResourceManager.Compute;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace AzureMessageStatsToTeams
{
    public class MessageTrigger
    {
        private readonly ILogger _logger;
        public readonly IConfiguration _configuration;

        public DateTimeOffset TimeStamp { get; private set; }

        public MessageTrigger(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<MessageTrigger>();
            _configuration = configuration;
        }
        
        //Trigger set to run once every hour
        [Function("MessageTrigger")]
        public async Task RunAsync([TimerTrigger("0 0 * * * *", RunOnStartup = true)] TimerInfo myTimer)
        {
            _logger.LogInformation("C# timer function activated.");

            //Load Template from Embedded Resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "AzureMessageStatsToTeams.adaptiveCardTemplate.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                throw new FileNotFoundException(resourceName);

            using var reader = new StreamReader(stream);
            var templateJson = reader.ReadToEnd();

            // Create a template instance from the template
            var template = new AdaptiveCardTemplate(templateJson);

            var dateFrom = DateTime.UtcNow.AddDays(-1);
            var dateTo = DateTime.UtcNow;

            var serverData = await GetToHighestCPUMinutes(dateFrom, dateTo, _configuration["serverId"]);

            // Create a data model and apply it to the template
            var cardJson = template.Expand(new
            {
                Title = $"VM Metrics between {dateFrom.ToString("g")} and {dateTo.ToString("g")}",
                Message = "Below are the the top CPU minutes for the last 24 hours.",
                Data = serverData
            });

            //Wrap the adaptive card in a teams message
            var wrappedTemplate = @"
            {
                ""type"": ""message"",
                ""attachments"": [
                    {
                        ""contentType"": ""application/vnd.microsoft.card.adaptive"",
                        ""content"": " + cardJson + @"
                    }
                ]
            }";

            //Post the message to teams
            var payload = new StringContent(wrappedTemplate.Trim(), System.Text.Encoding.UTF8, "application/json");

            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(_configuration["TeamsChannelUri"], payload);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Failed to post to teams: {response.StatusCode}");
            else
            {
                var responseText = await response.Content.ReadAsStringAsync();
                if (responseText != "1")
                {
                    throw new Exception($"Failed to post to teams: {responseText}");
                }
                _logger.LogInformation($"Response from teams: {responseText}");
            }
        }

        private async Task<MetricsModel[]> GetToHighestCPUMinutes(DateTime fromDate, DateTime toDate, string serverId)
        {
            var result = new List<MetricsModel>();
            var identity = new AzureCliCredential();//new DefaultAzureCredential();
            var armClient = new ArmClient(identity);
            var vm = armClient.GetVirtualMachineResource(new ResourceIdentifier(serverId));
            var metricsQueryClient = new MetricsQueryClient(identity);

            //Get the CPU for each minute (avarage over that minute)
            var queryResults = await metricsQueryClient.QueryResourceAsync(
                    vm.Id,
                    new[] { "Percentage CPU" }, new MetricsQueryOptions()
                    {
                        Aggregations = { MetricAggregationType.Average },
                        TimeRange = new QueryTimeRange(fromDate, toDate)
                    }
                );

            //Extract the values
                foreach (var metric in queryResults.Value.Metrics)
                {
                    foreach (var element in metric.TimeSeries)
                    {
                        foreach (var metricValue in element.Values)
                        {
                            result.Add(new MetricsModel() { TimeStamp = metricValue.TimeStamp, Value = metricValue.Average });
                        }
                    }
                }

            //select the top 10 values
            return result.OrderByDescending(a=>a.Value).Take(10).ToArray();
        }

        public class MetricsModel
        {
            public double? Value { get; set; }
            public DateTimeOffset TimeStamp { get; set; }
        }
    }
}
