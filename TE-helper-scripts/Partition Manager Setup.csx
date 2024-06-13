#r "Microsoft.AnalysisServices.Core.dll"

using System.Text.RegularExpressions;
using System.Globalization;

TabularEditor.TOMWrapper.ExpressionKind expressionKind = new TabularEditor.TOMWrapper.ExpressionKind();

var partitionManagerStartExpressionName = "PartitionManager_Placeholder_StartDate";
var partitionManagerEndExpressionName = "PartitionManager_Placeholder_EndDate";

if ( !Model.Expressions.Contains( partitionManagerStartExpressionName ) ) {
    Model.AddExpression(partitionManagerStartExpressionName, "#date(2021,12,31)");
    Model.Expressions[partitionManagerStartExpressionName].Expression = "#date(2021,12,31)";
    Model.Expressions[partitionManagerStartExpressionName].Kind = expressionKind;
}

if ( !Model.Expressions.Contains( partitionManagerEndExpressionName ) ) {
    Model.AddExpression(partitionManagerEndExpressionName, "#date(2021,12,31)");
    Model.Expressions[partitionManagerEndExpressionName].Expression = "#date(2021,12,31)";
    Model.Expressions[partitionManagerEndExpressionName].Kind = expressionKind;
}

// Created necessary annotations and set default values.
foreach( var t in Selected.Tables) {
    t.SetAnnotation("ProcessPartition_Range","Year");
    t.SetAnnotation("ProcessPartition_ISOStartDate","2021-1-1");
    t.SetAnnotation("ProcessPartition_Offset","0");
    t.SetAnnotation("ProcessPartition_FuturePartitionOffset","0");
    
    // Check for Batch on table
    // TODO
    
    // Check for placeholder values in M query
    var tableTemplatePartition = t.Partitions.First().Query;
    
    if ( !Regex.IsMatch(tableTemplatePartition , "PartitionManager_Placeholder") ) {
        var message = "One or more placeholder values are missing in the M query of " + t.Name + ". Check if PartitionManager_Placeholder_StartDate and PartitionManager_Placeholder_EndDate are used.";
        message.Output();
    }
}