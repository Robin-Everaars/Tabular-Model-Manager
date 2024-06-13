string batchPrefix = "TabularProcessingBatch_";
string newline = Environment.NewLine;

var validBatches = Model.GetAnnotations().Where(annotation => annotation.Contains(batchPrefix));

var sb_Info = new System.Text.StringBuilder();

foreach (var table in Model.Tables) {
    var tableBatches = table.GetAnnotations().Where(annotation => annotation.Contains(batchPrefix));
    var invalidTableBatches = tableBatches.Except(validBatches);
    
    // Initialize variable to later add increments to verify if any of the partitions has a valid batch
    var tablePartitionBatches = 0;
    
    // Check if the table or underlying partition has been assigned to any batch
    if ( tableBatches.Count() == 0 )
    {
        foreach ( var partition in table.Partitions.ToList() )
        {
            var partitionBatches = partition.GetAnnotations().Where(annotation => annotation.Contains(batchPrefix));
            
            tablePartitionBatches = tablePartitionBatches + partitionBatches.Count();
            
            var invalidPartitionBatches = partitionBatches.Except(validBatches);
            
            if ( invalidPartitionBatches.Count() > 0 ) {
                sb_Info.Append( 
                    "Found " + invalidPartitionBatches.Count() + " invalid batch(es) in '" 
                    + table.Name + "'" + "[" + partition.Name + "]:" 
                    + newline + String.Join( newline , invalidPartitionBatches ) + newline 
                );
            }
        }
    }
    // Check for assignment of invalid batches (batches that do not exist on a model level)
    else if ( invalidTableBatches.Count() > 0 ) {
        sb_Info.Append( 
            "Found " + invalidTableBatches.Count() + " invalid batch(es) in '" 
            + table.Name + "':" 
            + newline + String.Join( newline , invalidTableBatches ) + newline 
        );
    }
    
    if ( tableBatches.Count() == 0 && tablePartitionBatches == 0 ) {
        sb_Info.Append( 
            "Found no valid batch(es) in '" 
            + table.Name + "' or any underlying partitions." 
            + newline 
        );
    }    
}

sb_Info.Output();