Mdbc Release Notes
==================

## v1.0.3

New switch Split in the script Update-MongoFiles.ps1 tells to perform parallel
data processing and improve performance for large directories or entire drives.
It requires the PowerShell module SplitPipeline (it is not needed if Split is
not used). SplitPipeline may be useful for other database related tasks, too,
because processing of large data sets is typical.

## v1.0.2

C# driver v1.3.1.

## v1.0.1

C# driver v1.3.

Minor changes in tests and project infrastructure.