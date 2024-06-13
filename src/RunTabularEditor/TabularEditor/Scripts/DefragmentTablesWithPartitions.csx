#r "Microsoft.AnalysisServices.Core.dll"

// Annotation used for marking table for defragmentation by Processing Manager
var batchName = System.Environment.GetEnvironmentVariable("batchName");
var batchNameFull = "TabularProcessingBatch_" + batchName;
var refType = TOM.RefreshType.Defragment;

// Relevant tables have more than one partition and should have the current batch assigned.
var tablesWithPartitions = Model.Tables.Where(a => a.Partitions.Where(partition => partition.HasAnnotation(batchNameFull) ).Count() > 0 ).Where(b => b.Partitions.Count() > 1 ).Select(c => c.Name);

// Get tables
var defragmentTables = Model.Database.TOMDatabase.Model.Tables.Where(table => tablesWithPartitions.Contains(table.Name)).ToArray();

if ( defragmentTables.Count() > 0 ) {
    // Defragment
    var tmslDefrag = TOM.JsonScripter.ScriptRefresh(defragmentTables,refType);

    ExecuteCommand(tmslDefrag, false);

    // Calculate after defragment
    refType = TOM.RefreshType.Calculate;
    var tmslRecalc = TOM.JsonScripter.ScriptRefresh(defragmentTables,refType);

    ExecuteCommand(tmslRecalc, false);

    Info("Tables for batch " + batchName + " have been defragmented.");

    return;
}
else {
    Info("No tables for batch " + batchName + " have been found that needed to be defragmented.");

    return;
}