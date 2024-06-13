#r "Microsoft.AnalysisServices.Core.dll"
using System.Text.RegularExpressions;
using System.Globalization;

// Processing Manager parameters
var batchName = System.Environment.GetEnvironmentVariable("batchName");
string batchPrefix = "TabularProcessingBatch_";
string batchNameFull = batchPrefix + batchName;

// Options that can be set for saving changes back to the Tabular Model whilst connected.
TOM.SaveOptions SaveOptions = new TOM.SaveOptions();

// Determine if connected with server or local .bim.
var isLocalModel = string.IsNullOrEmpty(Model.Database.ServerName);

// Name of the model / database.
var databaseName = Model.Database.Name;

// Annotations used
var processPartitionRange = "ProcessPartition_Range";
var processPartitionStartDate = "ProcessPartition_ISOStartDate";
var processPartitionOffset = "ProcessPartition_Offset";
var processPartitionFutureOffset = "ProcessPartition_FuturePartitionOffset";

// For input validation
string[] acceptedPartitionRanges = { "year" , "month" , "day" };

// Date until which last partition needs to exist for each table.
var now = DateTime.UtcNow;

// Initialize variable for TMSL for all tables.
var tmslMergeForAllTables = "";

foreach (var t in Model.Tables) {
    if ( t.HasAnnotation(processPartitionRange) && t.HasAnnotation( processPartitionStartDate ) ) {
        // Temporarily put the batch annotation on the table if any of the partitions have it.
        var partitionsWithBatchAnnotationCount = 0;

        foreach ( var Partition in t.Partitions ) {
            if ( Partition.HasAnnotation(batchNameFull) ) {
                partitionsWithBatchAnnotationCount = partitionsWithBatchAnnotationCount + 1;
            }
        }

        if ( partitionsWithBatchAnnotationCount > 0 ) {
            t.SetAnnotation(batchNameFull,"1");
        }

        // Get partition range input
        var partitionRange = t.GetAnnotation( processPartitionRange ).ToLower();

        // Validate partition range input. Throw error when not in accepted partition ranges.
        if ( !acceptedPartitionRanges.Contains( partitionRange ) ) {
            Error( "Invalid Partition Range \"" + t.GetAnnotation( processPartitionRange ) + "\" for " + t.Name + ". Please set the Partition Range to one of the accepted values: " + String.Join( ", " , acceptedPartitionRanges ) );
            return;
        };

        // Initialize range for which partitions need to be created based on first date given till now. Throw error on wrong input.
        DateTime firstPartitionDate;
        if ( !DateTime.TryParseExact(t.GetAnnotation(processPartitionStartDate), "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out firstPartitionDate) ) {
            Error("Invalid date input given \"" + t.GetAnnotation(processPartitionStartDate) + "\" for " + t.Name + ". Expected date format is \"yyyy-M-d\".");
            return;
        }

        // Get first partition for the current table as template.
        var tableTemplatePartition = t.Partitions.First().Query;

        // Validate presence of Partition Manager placeholder values in template partition.
        if ( !Regex.IsMatch(tableTemplatePartition , "PartitionManager_Placeholder") ) {
            Error("One or more placeholder values are missing in the M query. Check if PartitionManager_Placeholder_StartDate and PartitionManager_Placeholder_EndDate are used.");
            return;
        }

        // Get partition offset for refresh
        int refreshOffset;
        if ( !int.TryParse(t.GetAnnotation(processPartitionOffset), out refreshOffset) ) {
            Error("Invalid offset input given \"" + t.GetAnnotation(processPartitionOffset) + "\" for " + t.Name + ". Expected format is a valid integer.");
            return;
        }

        // Get future partition offset for refresh
        int futurePartitionOffset;
        if ( !int.TryParse(t.GetAnnotation(processPartitionFutureOffset), out futurePartitionOffset) ) {
            Error("Invalid future partition offset input given \"" + t.GetAnnotation(processPartitionFutureOffset) + "\" for " + t.Name + ". Expected format is a valid integer.");
            return;
        }

        var refreshPartitionDate = now; // Date from which partitions need to be refreshed.

        refreshOffset = refreshOffset * (refreshOffset > 0 ? -1 : 1); // Set offset to negative integer when positive integer is given as input.

        // Full year difference for defining what year partitions should exist.
        var yearDifference = now.Year - firstPartitionDate.Year; // Equals the number of year partitions that should be created.

        // Generate list of partitions to be created
        var partitionsToExistList = new List<String>();

        // ... for years
        if ( partitionRange == "year" ) {
            for (var i = 0; i <= (yearDifference + futurePartitionOffset); i++) {
                partitionsToExistList.Add((firstPartitionDate.Year + i).ToString());
            }

            refreshPartitionDate = now.AddYears(refreshOffset + futurePartitionOffset); // Date from which partitions need to be refreshed.
        }
        else {
            for (var i = 0; i < yearDifference; i++) {
                partitionsToExistList.Add((firstPartitionDate.Year + i).ToString());
            }
        }

        //TODO: take offset into account: have list with previous year-month and year-month-day at the ready?

        // ... for months and days
        if ( partitionRange == "month" ) {
            for (var i = 1; i <= now.Month; i++) {
                partitionsToExistList.Add(now.Year.ToString() + i.ToString("D2"));
            }

            refreshPartitionDate = now.AddMonths(refreshOffset); // Date from which partitions need to be refreshed.
        }
        else if ( partitionRange == "day" ) {
            for (var i = 1; i < now.Month; i++) {
                partitionsToExistList.Add(now.Year.ToString() + i.ToString("D2"));
            }
            for (var i = 1; i <= now.Day; i++) {
                partitionsToExistList.Add(now.Year.ToString() + now.Month.ToString("D2") + i.ToString("D2"));
            }

            refreshPartitionDate = now.AddDays(refreshOffset); // Date from which partitions need to be refreshed.
        }

        // Check for existing partitions
        var existingPartitionList = t.Partitions.Select(partition => partition.Name);

        // Create new partitions that do not yet exist
        var newPartitionsToBeCreatedList = partitionsToExistList.Except(existingPartitionList);

        foreach ( var partition in newPartitionsToBeCreatedList ) {
            var newAddedPartition = t.AddMPartition( partition.ToString() , t.Name );

            // Set values for start date
            var partitionName = newAddedPartition.Name + "0101";
            var startDate = DateTime.Parse( partitionName.Substring(0,4) + "-" + partitionName.Substring(4,2) + "-" + partitionName.Substring(6,2) );

            // Set partition query to correct date range for start date
            var partitionCurrentPartitionQuery = Regex.Replace(
                tableTemplatePartition,
                    "[^*/]PartitionManager_Placeholder_StartDate[^*/]", // Replace the placeholder with the start date for the current period
                    "#date(" + startDate.Year.ToString() + "," + startDate.Month.ToString() + "," + startDate.Day.ToString() + ") /*PartitionManager_Placeholder_StartDate*/"
                 );

            partitionCurrentPartitionQuery = Regex.Replace(
                partitionCurrentPartitionQuery ,
                    @"\#date\(\d{4},\d{1,2},\d{1,2}\) /\*PartitionManager_Placeholder_StartDate\*/", // Replace the placeholder with the start date for the current period in case it already had been replaced once
                    "#date(" + startDate.Year.ToString() + "," + startDate.Month.ToString() + "," + startDate.Day.ToString() + ") /*PartitionManager_Placeholder_StartDate*/"
            );

            // Set new values for end date
            var endDate = startDate; // For day get the same date as start for end.

            if ( partitionRange == "month" ) {
                endDate = endDate.AddMonths(1).AddDays(-1); // Get latest date for month partition.
            }
            else if ( partitionRange == "year" ) {
                endDate = endDate.AddYears(1).AddDays(-1); // Get latest date for year partition.
            }

            // Set partition query to correct date range for end date
            partitionCurrentPartitionQuery = Regex.Replace(
                partitionCurrentPartitionQuery,
                    "[^*/]PartitionManager_Placeholder_EndDate[^*/]", // Replace the placeholder with the end date for the current period
                    "#date(" + endDate.Year.ToString() + "," + endDate.Month.ToString() + "," + endDate.Day.ToString() + ") /*PartitionManager_Placeholder_EndDate*/"
                 );

            partitionCurrentPartitionQuery = Regex.Replace(
                partitionCurrentPartitionQuery ,
                    @"\#date\(\d{4},\d{1,2},\d{1,2}\) /\*PartitionManager_Placeholder_EndDate\*/", // Replace the placeholder with the end date for the current period in case it already had been replaced once
                    "#date(" + endDate.Year.ToString() + "," + endDate.Month.ToString() + "," + endDate.Day.ToString() + ") /*PartitionManager_Placeholder_EndDate*/"
            );

            newAddedPartition.Query = partitionCurrentPartitionQuery;
        }

        // Set batchName for valid partitions after refreshPartitionDate and remove from partitions before.
        var parsedPartitionName = 0; // Only exists for checking if the batch is valid for parsing to date.
        foreach ( var refreshPartition in t.Partitions.Where(p => int.TryParse(p.Name, out parsedPartitionName) ) ) {
            if ( DateTime.Parse( refreshPartition.Name.Substring(0,4) + "-" + (refreshPartition.Name + "12").Substring(4,2) + "-" + ( refreshPartition.Name + (refreshPartition.Name.Length > 6 ? "31" : "1231") ).Substring(6,2) ) > refreshPartitionDate && t.HasAnnotation(batchNameFull) ) {
                refreshPartition.SetAnnotation(batchNameFull,"1");
            }
            else {
                refreshPartition.RemoveAnnotation(batchNameFull);
            }
        }

        // Partitions that need to be merged whilst connected with a Tabular Model or deleted locally
        var existingPartitionsToBeMergedList = existingPartitionList.Except(partitionsToExistList);

        // Create merge request for current table.
        if ( !isLocalModel ) {
            // Create partition merge request for each target; put together one single request ordered by days into months and then months into years.
            var tmslForCurrentTable = "";

            // Create partition merge request for each target; put together one single request ordered by days into months and then months into years.
            foreach ( var np in partitionsToExistList.Where( p => p.Length < 8 ) ) { // Don't merge day partitions (length = 8 = yyyymmdd)
                var mergeSourceList = existingPartitionsToBeMergedList.Where( ep => ep.Substring(0,(np.Length)) == np ).ToList();

                if ( mergeSourceList.Count > 0 ) {
                    var mergeTarget =
                        "{ \"mergePartitions\": { \"target\": { \"database\": " +
                        "\"" + databaseName + "\"," +
                        "\"table\": " +
                        "\"" + t.Name + "\"," +
                        "\"partition\": " +
                        "\"" + np + "\"" +
                        "}"
                        ;

                    var mergeSources = "";

                    foreach ( var mp in mergeSourceList ) {
                        mergeSources = mergeSources + (mergeSources.Length > 1 ? "," : "") +
                            "\"" + mp + "\""
                            ;
                    }

                    var tmslForCurrentTargetPartition =
                        mergeTarget + ", \"sources\": [" + mergeSources + "] } }";

                    tmslForCurrentTable = tmslForCurrentTable + (tmslForCurrentTargetPartition.Length > 1 ? "," : "") + tmslForCurrentTargetPartition;

                    tmslMergeForAllTables = tmslMergeForAllTables + (tmslForCurrentTable.Length > 1 ? "," : "") + tmslForCurrentTable;
                }
            }
        }
        else {
            // Delete local partitions that should otherwise be merged
            foreach ( var partition in existingPartitionsToBeMergedList.ToList() ) {
                t.Partitions[partition.ToString()].Delete();
            }
        }

        // Remove the (temporary) batch annotation from the table (which now exists on the partition).
        t.RemoveAnnotation(batchNameFull);
    }
}

// TODO remove any partitions that are not valid (i.e. called "Partition") to prevent any issues.

// Save new (empty) partitions back to model when not running locally and send TSML for merging partitions when not running locally
if ( !isLocalModel ) {
    Model.Database.TOMDatabase.Model.SaveChanges(SaveOptions); // Save new (empty) partitions back to model.

    var countPartitionsToMerge = new Regex(Regex.Escape("mergePartitions")).Matches(tmslMergeForAllTables).Count;
    var sequenceStart = countPartitionsToMerge > 1 ? "{ \"sequence\": " : "";
    var sequenceEnd = countPartitionsToMerge > 1 ? " }" : "";
    var tmsl = sequenceStart + tmslMergeForAllTables.TrimStart(',') + sequenceEnd; // Concatenate all seperate table requests into one single request.

    ExecuteCommand(tmsl, false); // Execute the merge partition request in sequence.
}