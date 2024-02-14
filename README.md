This is a sample showing how to post an adaptive card message to Microsoft teams that contains a table of data from Azure metrics.

To run the code create an `local.settings.json` adding your TeamsChannelUri to post to and the resourceId of the VM you want to get the metrics from.

```json
{
  "Values": {
    "TeamsChannelUri": "https://outlook.office.com/webhook/xxxxx/IncomingWebhook/xxxxx",
    "serverId": "/subscriptions/xxxxxx/resourceGroups/xxxxxx/providers/Microsoft.Compute/virtualMachines/xxxxxx",
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
  }
}
```

Once setup use `func start` to run the function and post the adaptive card to the Teams channel.