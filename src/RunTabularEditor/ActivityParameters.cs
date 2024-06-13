namespace RunTabularEditor;

public class ActivityParameters : JobParameters
{
    public static ActivityParameters CreateFromJobParameters(JobParameters jobParameters, string script) {
        return new ActivityParameters {
            BaseURL = jobParameters.BaseURL,
            Server = jobParameters.Server,
            Model = jobParameters.Model,
            BatchName = jobParameters.BatchName,  
            Script = script
        };
    }

    public string Script { get; set; }
}