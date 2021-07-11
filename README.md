Generator for strongly typed access to all stored procedures in a SQL Server, intended for use with [Insight.Database Auto Interface Implementation](https://github.com/jonwagner/Insight.Database/wiki/Auto-Interface-Implementation).

Goals:
- Make it obvious which stored procedure C# code is calling
- There should be a C# compilation error when the parameters or result sets of stored procedure are changed in a way that would break at runtime

---

Really not an ORM, this strives to be a non-leaky abstraction for stored procedures in SQL Server including all table valued parameters, output parameters and multiple result sets.  The resulting types and namespaces are not idiomatic C# / .NET but the generated code uses partial classes to allow hand written code (e.g. additional interfaces) to simplify access from .NET. 

Includes:
- [Visitor base class](./Cameronism.TolerableDB.SqlServer/StoredProcedureVisitor.cs) to enumerate all stored procedures, parameters, table valued parameters, and result sets in a SQL Server 
- [Visitor implementation](./Cameronism.TolerableDB.SqlServer/InsightInterfaceBuilder.cs) that generates [interfaces for Insight.Database](https://github.com/jonwagner/Insight.Database/wiki/Auto-Interface-Implementation)
- Command line interface for :point_up:

## Interface Generator

Example output:
-   `all-params.json`
-   `Repositories/`
    -   _one cs file per schema_
    -   `IFooRepository.cs`
    -   `IBarRepository.cs`
-   `TableTypes/`
    -  _one dir per schema (with encountered table type)_
    -  `foo/`
    -  `bar/`
        - _one interface file and one class file per TVP type encountered_
-   `ResultSets/`
    - _one dir per schema (with stored procedure results)_
    - `foo/`
    - `bar/`
        - _one cs file for each SP returning at least one table_
        - _each file will contain one or more types (corresponding to the number of types returned by the table)_
-   `OutputParameters/`
    - _one dir per schema (with stored procedure output parameters)_
    - `foo/`
    - `bar/`
        - _one interface file and one class file for each SP with at least one output parameter_



## Comand Line Interface

```
$ Cameronism.TolerableDB.SqlServer.exe --help
  -c, --connection        Required. Connection string of database to introspect
  -d, --directory         Required. Destination folder for generated files
  -n, --namespace         Namespace for generated files
  -e, --generateErrors    Generate warnings or errors for problems
  --help                  Display this help screen.
```

Usage:
```
$ Cameronism.TolerableDB.SqlServer.exe \
  --connection "your connection string" \
  --directory C:\your\destination \
  --namespace Your.Namespace \
  --generateErrors false
```

## Status

Pre-alpha. Works on my machine


## Limitations

[`sp_describe_first_result_set`](https://docs.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-describe-first-result-set-transact-sql?view=sql-server-ver15)

`sp_describe_first_result_set` doesn't work in at least the following cases:
- **NOT auto detected.** Multiple result sets, stored procedures that return more than one result set (commonly multiple `select` statements) are not automatically handled
- **Auto detected.** Stored procedures that [use a temporary table](https://docs.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-describe-first-result-set-transact-sql?view=sql-server-ver15#remarks).  Table variables are sometimes an acceptable work around for this limitation
- **Auto detected.** Stored procedures that use dynamic SQL
- **Auto detected.** Incompatible result sets.  e.g. incompatible `select` statements in different branches of `if` ... `else`
- **Auto detected.** Stored procedures that invoke extended stored procedures.  e.g. [`sp_getapplock`](https://docs.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver15)

Generally, these limitations should be addressed by providing a sample sql script that invokes the stored procedure.  [The sample script will be invoked inside a transaction and then rolled back](https://github.com/cameronism/Cameronism.TolerableDB.SqlServer/blob/5cd5051be00b796adf6d86188544a2aba5a80937/Cameronism.TolerableDB.SqlServer/StoredProcedureVisitor.cs#L109-L121).  While sample scripts SHOULD execute the corresponding stored procedure since scripts can ignore side effects and stay in sync or appropriately fail when the stored procedure changes, scripts MAY fake it by returning data that exactly matches what the corresponding stored procedure will return.  The number of rows returned does not matter only the number of result sets and their corresponding column names and data types.

By default, the generator expects sample scripts in a directory named `SampleScripts` next to the generated `OutputParameters`, `Repositories`, etc. directories.  For example the sample script for a stored procedure named `foo.Select_Something` should be located at `C:\your\destination\SampleScripts\foo.Select_Something.sql`

## Next Steps

- [ ] Dog fooding.  Use generator instead of
  [hand](./Cameronism.TolerableDB.SqlServer/StoredProcedureParameterInfo.cs) 
  [written](.Cameronism.TolerableDB.SqlServer/StoredProcedureResult.cs)
  [classes](./Cameronism.TolerableDB.SqlServer/TableTypeColumn.cs).  
  This will probably be a one off or optional mode since it is not reasonable to expect users to install custom stored procedures for this tool
- [ ] Sample DB 
- [ ] Tests based on sample DB

