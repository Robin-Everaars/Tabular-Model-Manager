using System.IO;
using System.Text;
using System.Data;

var swTotal = new System.Diagnostics.Stopwatch();
swTotal.Start();

int factor = (int)Math.Pow(10,(7 - 2));

// Data Quality Manager parameters
string rulePrefix = "DataQualityManager_";

string ruleMessage = "";

foreach( var ruleName in Model.GetAnnotations().Where( a => a.StartsWith( rulePrefix ) ) ) {
    var dqRuleExpression = Model.GetAnnotation( ruleName );

    var swModelRule = new System.Diagnostics.Stopwatch();
    swModelRule.Start();

    var numberOfViolations = EvaluateDax ( "COUNTROWS ( " + dqRuleExpression + " )" );

    swModelRule.Stop();

    TimeSpan durationModelRule = new TimeSpan(((long)Math.Round((1.0*swModelRule.Elapsed.Ticks/factor))*factor));

    if ( numberOfViolations != "0" && numberOfViolations != null ) {
    ruleMessage = ruleMessage + Environment.NewLine + numberOfViolations + " Violations found for rule " + ruleName + ". (" + durationModelRule.ToString().Remove(durationModelRule.ToString().Length -5) + ")";
    }
}

foreach ( var table in Model.Tables ) {
    string tableName = table.Name;

    var swTableEmpty = new System.Diagnostics.Stopwatch();
    swTableEmpty.Start();

    var numberOfRowsInTable = EvaluateDax ( "COUNTROWS ( '" + tableName + "' )" );

    swTableEmpty.Stop();

    TimeSpan durationTableEmpty = new TimeSpan(((long)Math.Round((1.0*swTableEmpty.Elapsed.Ticks/factor))*factor));

    if ( numberOfRowsInTable == null ) {
        ruleMessage = ruleMessage + Environment.NewLine + "'" + tableName + "' contains no records. (" +  durationTableEmpty.ToString().Remove(durationTableEmpty.ToString().Length -5) + ")";
    }

    foreach( var tableRuleName in table.GetAnnotations().Where( a => a.StartsWith( rulePrefix ) ) ) {
        var dqTableRuleExpression = table.GetAnnotation( tableRuleName );

        var swTable = new System.Diagnostics.Stopwatch();
        swTable.Start();

        var numberOfTableViolations = EvaluateDax ( "COUNTROWS ( " + dqTableRuleExpression + " )" );

        swTable.Stop();

        TimeSpan tableDuration = new TimeSpan(((long)Math.Round((1.0*swTable.Elapsed.Ticks/factor))*factor));

        if ( numberOfTableViolations != "0" && numberOfTableViolations != null ) {
        ruleMessage = ruleMessage + Environment.NewLine + numberOfTableViolations + " Violations found for rule " + tableRuleName + ". (" + tableDuration.ToString().Remove(tableDuration.ToString().Length -5) + ")";
        }
    }
}

swTotal.Stop();

TimeSpan totalTimeSpan = swTotal.Elapsed;

string timeSpent = "";

int sec = totalTimeSpan.Seconds;
int min = totalTimeSpan.Minutes;
int hr = totalTimeSpan.Hours;

// Break down hours,minutes,seconds
if (hr == 0)
{
    if (min == 0)
    {
        timeSpent = sec + " seconds";
    }
    else
    {
        timeSpent = min + " minutes and " + sec + " seconds";
    }
}
else
{
    timeSpent = hr + " hours, " + min + " minutes and " + sec + " seconds";
}

if (hr == 1)
{
    timeSpent = timeSpent.Replace("hours","hour");
}
if (min == 1)
{
    timeSpent = timeSpent.Replace("minutes","minute");
}
if (sec == 1)
{
    timeSpent = timeSpent.Replace("seconds","second");
}

if ( ruleMessage != "" ) {
    Info( @"The following Data Quality violations have been found in " + timeSpent + ":" + ruleMessage );

    return;
}
else {
    Info( @"No Data Quality violations have been found in " + timeSpent + "." );

    return;
}