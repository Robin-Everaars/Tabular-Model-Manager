using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Net;

namespace RunTabularEditor;

public static class RunTabularEditor
{
    public static string AssemblyDirectory
    {
        get
        {
            string codeBase = Assembly.GetExecutingAssembly().Location;
            var uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            return Path.GetDirectoryName(path);
        }
    }

    [FunctionName("RunTabularEditor_Orchestrator")]
    public static async Task<string> RunOrchestrator(
        [OrchestrationTrigger] IDurableOrchestrationContext context,
        ILogger log
    )
    {
        var retryOptions = new RetryOptions(
                firstRetryInterval: TimeSpan.FromSeconds(30),
                maxNumberOfAttempts: 2
            );

        // Get data from trigger.
        var jobParams = context.GetInput<JobParameters>();

        // Partition Manager
        context.SetCustomStatus("1/4: Running Partition Manager");
        var partition = await context.CallActivityWithRetryAsync<string>("RunTabularEditor_Activity", retryOptions, ActivityParameters.CreateFromJobParameters(jobParams, "AutoPartitioning.csx"));

        // Processing Manager
        context.SetCustomStatus("2/4: Running Processing Manager");
        var process = await context.CallActivityWithRetryAsync<string>("RunTabularEditor_Activity", retryOptions, ActivityParameters.CreateFromJobParameters(jobParams, "ProcessBatches.csx"));

        // Defragment Partitions
        context.SetCustomStatus("3/4: Defragementing Partitions");
        var defragment = await context.CallActivityWithRetryAsync<string>("RunTabularEditor_Activity", retryOptions, ActivityParameters.CreateFromJobParameters(jobParams, "DefragmentTablesWithPartitions.csx"));

        // Data Quality Manager
        context.SetCustomStatus("4/4: Running Data Quality Manager");
        var quality = await context.CallActivityWithRetryAsync<string>("RunTabularEditor_Activity", retryOptions, ActivityParameters.CreateFromJobParameters(jobParams, "DataQualityChecks.csx"));

        // Returns output of the Activity functions.
        context.SetCustomStatus("All activities completed succesfully");
        var seperator = "\n----------------------------------------------------------------\n";
        return partition + seperator + process + seperator + defragment + seperator + quality;
    }

    [FunctionName("RunTabularEditor_Activity")]
    public static string RunActivity([ActivityTrigger] ActivityParameters parameters, ILogger log)
    {
        // Declare variables for working directory and .exe to execute.
        var workingDirectoryInfo = Path.GetFullPath(AssemblyDirectory);
        var exeLocation = Path.Combine(workingDirectoryInfo, "../TabularEditor/TabularEditor.exe");
        var scriptLocation = Path.Combine(workingDirectoryInfo, "../TabularEditor/Scripts/" + parameters.Script);

        // Set Request Body as variables and get secrets from Keyvault reference.
        var clientId = Environment.GetEnvironmentVariable("ClientId"); // Get ClientId from Keyvault reference.
        var tenantId = Environment.GetEnvironmentVariable("TenantId"); // Get TenantId from Keyvault reference.
        var clientSecret = Environment.GetEnvironmentVariable("ClientSecret"); // Get ClientSecret from Keyvault reference.

        // Set batchName as Environment Variable so Tabular Editor can use it.
        Environment.SetEnvironmentVariable(
            "batchName",
            parameters.BatchName,
            EnvironmentVariableTarget.Process
        );

        // Set connection string for Power BI Premium or Azure Analysis Services
        var connectionString = 
            "\"Provider = MSOLAP; Data Source = " + parameters.BaseURL
            + parameters.Server
            + "; User ID=app:"
            + clientId
            + "@"
            + tenantId
            + "; Password="
            + clientSecret
            + "\" \""
            + parameters.Model
            + "\" -S "
        ;       

        // Values that needs to be set before starting Tabular Editor.
        var info = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectoryInfo,
            FileName = exeLocation,
            Arguments =
                connectionString
                + scriptLocation
                + " -E", // -E returns a non zero exit code when an error is encountered.
            WindowStyle = ProcessWindowStyle.Minimized,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Start Tabular Editor.
        using var proc = new Process { StartInfo = info };

        //  Discard any information about the associated process that has been cached inside the process component.
        proc.Refresh();

        // For the textual output of an application to written to the System.Diagnostics.Process.StandardOutput stream.
        proc.StartInfo.RedirectStandardOutput = true;

        // Starts the Process, with the above configured values.
        proc.Start();

        // Build the string from the log messages.
        var stringBuilder = new StringBuilder();

        // Scanning the entire stream and reading the output of application process and writing it to a local variable.
        while (!proc.StandardOutput.EndOfStream)
        {
            stringBuilder.Append(proc.StandardOutput.ReadLine());
            stringBuilder.Append(Environment.NewLine);
        }

        // Wait for Tabular Editor to be done doing it's thing.
        proc.WaitForExit();

        // Check if any exceptions were thrown
        var exitcode = proc.ExitCode;
        var exitstring = "";
        
        if ( exitcode == 0 ) {
            exitstring = "Success";
        } else {
            exitstring = "Failed";
        }

        // Initialize the final output message.
        string output =
            $"TabularEditor.exe {DateTime.Now}: \n ExitCode: {exitcode} ({exitstring})\n Output:\n {stringBuilder.Replace(clientSecret, "***")}";

        //Logging Output.
        log.LogInformation($"RunTabularEditor function executed at: {DateTime.Now} \n {output}");

        if ( exitcode != 0 )
        {
            throw new Exception(output);
        }

        return output;
    }

    [FunctionName("RunTabularEditor_HttpStart")]
    public static async Task<HttpResponseMessage> TriggerOrchestration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
        [DurableClient] IDurableOrchestrationClient starter,
        ILogger log
    )
    {
        using var stream = await req.Content.ReadAsStreamAsync();
        var parameters = await JsonSerializer.DeserializeAsync<JobParameters>(stream);
        
        // Explanation given when one of the following input validation checks has failed
        var validParameterEntryExplanation = " When trying to connect to an Azure Analysis Services instance, make sure that 'BaseURL' is formatted like asazure://<region>.asazure.windows.net/ and that the 'Server', 'Model' and 'BatchName' parameters are valid. When trying to connect to a Power BI Premium dataset, make sure 'BaseURL' is formatted like powerbi://api.powerbi.com/v1.0/myorg/ and 'Server' (Power BI Workspace), 'Model' (Power BI Dataset) and 'BatchName' parameters are valid.";
        
        // Check for any empty required parameters. Valid combination is explained above.
        if ( 
            string.IsNullOrEmpty( parameters.BaseURL )
            || string.IsNullOrEmpty( parameters.Server )
            || string.IsNullOrEmpty( parameters.Model )
            || string.IsNullOrEmpty( parameters.BatchName )
        ) {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "One or parameters were missing." + validParameterEntryExplanation
                ),
            };
        }

        var instanceId = $"Process-{parameters.Server}-{parameters.Model}";

        var existingInstance = await starter.GetStatusAsync(instanceId);
        if (
            existingInstance == null
            || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Completed
            || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Failed
            || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Terminated
        ) {
            // Function input comes from the request content.
            await starter.StartNewAsync(
                "RunTabularEditor_Orchestrator",
                instanceId,
                parameters
            );

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
            return starter.CreateCheckStatusResponse(req, instanceId);
        }
        else {
            // An instance with the specified ID exists or an existing one is still running, don't create one.
            return new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    $"An instance with ID '{instanceId}' already exists."
                ),
            };
        }
    }
}