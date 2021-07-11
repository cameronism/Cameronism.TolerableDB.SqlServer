using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Cameronism.TolerableDB.SqlServer
{
    public class InsightInterfaceBuilder : StoredProcedureVisitor
    {
        /*
        dir/
            all-params.json
            Repositories/
                -- one cs file per schema
                IFooRepository.cs
                IBarRepository.cs
            TableTypes/
                -- one dir per schema (with encountered table type)
                foo/
                bar/
                    -- one interface file and one class file per TVP type encountered
                    ...
            ResultSets/
                -- one dir per schema (with stored procedure results)
                foo/
                bar/
                    -- one cs file for each SP returning at least one table
                    -- each file will contain one or more types (corresponding to the number of types returned by the table)
                    ...
            OutputParameters/
                -- one dir per schema (with stored procedure output parameters)
                foo/
                bar/
                    -- one interface file and one class file for each SP with at least one output parameter
                    ...

         */
        private readonly DirectoryInfo root;
        private readonly DirectoryInfo repositoryDir;
        private readonly DirectoryInfo tableTypesDir;
        private readonly DirectoryInfo resultTypesDir;
        private readonly DirectoryInfo outputTypesDir;
        private readonly Dictionary<string, TextWriter> repositoryFiles = new Dictionary<string, TextWriter>();
        private readonly List<TextWriter> codeFiles = new List<TextWriter>();
        private readonly List<string> resultTypes = new List<string>();
        private Dictionary<string, string> CustomTypeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<(string schema, string name), TextWriter> resultSetClassFiles = new Dictionary<(string schema, string name), TextWriter>();
        private bool resultTypeError;

        public InsightInterfaceBuilder(IDbConnection connection, DirectoryInfo dir) : base(connection)
        {
            root = dir;
            dir.Create();

            repositoryDir = CreateDirectory(dir, "Repositories");
            tableTypesDir = CreateDirectory(dir, "TableTypes");
            resultTypesDir = CreateDirectory(dir, "ResultSets");
            outputTypesDir = CreateDirectory(dir, "OutputParameters");
        }

        public string Namespace { get; set; } = "Some.Namespace";
        public bool ErrorOnDescribeResultFail { get; set; } = true;

        protected override Task VisitAllParameters(IList<StoredProcedureParameterInfo> parameters)
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "all-params.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(parameters));

            return base.VisitAllParameters(parameters);
        }

        public override async Task VisitAsync()
        {
            try
            {
                await base.VisitAsync();
            }
            finally
            {
                foreach (var writer in codeFiles)
                {
                    EndCodeFile(writer);
                    writer.Dispose();
                }
            }
        }

        protected override Task VisitSchemaAsync(string schema, IEnumerable<StoredProcedureParameterInfo> parameters)
        {
            var interfaceName = $"I{ToPascalCase(schema)}Repository";
            var writer = CreateTextFile(repositoryDir, $"{interfaceName}.generated.cs");
            repositoryFiles[schema] = writer;
            BeginCodeFile(
                repositoryDir,
                writer,
                $"[Sql(Schema=\"{schema}\")]",
                $"public interface {interfaceName}");
            codeFiles.Add(writer);

            return base.VisitSchemaAsync(schema, parameters);
        }

        private void BeginCodeFile(DirectoryInfo dir, TextWriter writer, params string[] boilerplate)
        {
            foreach (var ns in new[] { "System", "System.Collections.Generic", "System.Threading.Tasks", "Insight.Database" })
            {
                writer.WriteLine($"using {ns};");
            }
            writer.WriteLine();
            writer.WriteLine($"namespace {Namespace}.{GetRelativeNamespace(dir)}");
            writer.WriteLine($"{{");

            foreach (var value in boilerplate)
            {
                writer.WriteLine($"    {value}");
            }
            writer.WriteLine($"    {{");
        }

        private void EndCodeFile(TextWriter writer)
        {
            writer.WriteLine($"    }}");
            writer.WriteLine($"}}");
        }

        protected override async Task VisitProcedureAsync(string schema, string name, IEnumerable<StoredProcedureParameterInfo> parameters)
        {
            // Clear SP state
            resultTypes.Clear();
            resultTypeError = false;

            // Let visitor continue, generate new state
            await base.VisitProcedureAsync(schema, name, parameters);

            // Write method declaration 
            var parametersString = await GetParametersAsync(schema, name, parameters);
            var resultTypeName = GetResultName(resultTypes);
            WriteLine(2, repositoryFiles[schema], $"{resultTypeName} {name}({parametersString});");

            // consider adding overload(s) for common insight parameters here
            // https://github.com/jonwagner/Insight.Database/wiki/Auto-Interface-Implementation#special-parameters
            // probably useful:
            // - int? commandTimeout
            // - CancellationToken? cancellationToken
            // - IDbTransaction transaction
        }

        private string GetResultName(List<string> resultTypes)
        {
            if (resultTypes.Count == 1)
            {
                return $"Task<IList<{resultTypes[0]}>>";
            }
            if (resultTypes.Count > 1)
            {
                return $"Task<Results<{string.Join(", ", resultTypes)}>>";
            }
            if (!resultTypeError)
            {
                return "Task";
            }

            return "Task</* FIXME result type here */>";
        }

        private async Task<string> GetParametersAsync(string schema, string name, IEnumerable<StoredProcedureParameterInfo> parameters)
        {
            var items = new List<string>();
            var outputParams = new List<StoredProcedureParameterInfo>();
            foreach (var param in parameters)
            {
                if (param.is_output)
                {
                    outputParams.Add(param);
                    continue;
                }

                if (!GetStandardTypeName(param, out string typeName))
                {
                    // is there a better way to check for TVP?
                    if (param.is_readonly)
                    {
                        // TVP
                        typeName = await GetCustomTypeNameAsync(param);
                    }
                    else
                    {
                        throw new NotImplementedException($"Unknown .NET type for {param.type_name} in {schema}.{name} parameter {param.name}");
                    }
                }

                items.Add($"{typeName} {GetParameterName(param.name)}");
            }

            if (outputParams.Any())
            {
                var resultTypeName = GetOutputTypeName(schema, name, outputParams);
                items.Add($"{resultTypeName} output");
            }

            return string.Join(", ", items);
        }

        private string GetOutputTypeName(string schema, string name, List<StoredProcedureParameterInfo> outputParams)
        {
            var dir = new DirectoryInfo(Path.Combine(outputTypesDir.FullName, schema));
            dir.Create();

            return PrepareTypeInterfacePair(dir, name, outputParams, interfaceSetters: true);
        }

        private async Task<string> GetCustomTypeNameAsync(StoredProcedureParameterInfo param)
        {
            if (!CustomTypeNames.TryGetValue(param.type_name, out var typeName))
            {
                var columns = await GetTableTypeColumnsAsync(param);
                typeName = PrepareTypeInterfacePair(tableTypesDir, param.type_name, columns);
                CustomTypeNames[param.type_name] = typeName;
            }

            return $"IEnumerable<{typeName}>";
        }

        private string PrepareTypeInterfacePair(DirectoryInfo dir, string basename, IEnumerable<IDatabaseType> members, bool interfaceSetters = false)
        {
            // generate an interface and a class that implements it
            var interfaceAccessors = interfaceSetters ? "{ get; set; }" : "{ get; }";
            var interfaceName = $"I{basename}";
            using var classWriter = CreateTextFile(dir, $"{basename}.generated.cs");
            using var interfaceWriter = CreateTextFile(dir, $"{interfaceName}.generated.cs");

            BeginCodeFile(dir, classWriter, $"public partial class {basename} : {interfaceName}");
            BeginCodeFile(dir, interfaceWriter, $"public interface {interfaceName}");

            foreach (var member in members)
            {
                if (!GetStandardTypeName(member, out var typeName))
                {
                    throw new NotImplementedException($"Unknown .NET type for {member.type_name} in {basename}");
                }
                var memberName = member.name.TrimStart('@');
                WriteLine(2, classWriter, $"public virtual {typeName} {memberName} {{ get; set; }}");
                WriteLine(2, interfaceWriter, $"{typeName} {memberName} {interfaceAccessors}");
            }

            EndCodeFile(classWriter);
            EndCodeFile(interfaceWriter);

            return $"{GetRelativeNamespace(dir)}.{interfaceName}";
        }

        protected override Task VisitResultSetAsync(string schema, string name, int index, IList<StoredProcedureResult> columns)
        {
            WriteResultSet(schema, name, index, writer =>
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var column in columns)
                {
                    if (!GetStandardTypeName(column, out var typeName))
                    {
                        throw new NotImplementedException($"Unknown .NET type for {column.system_type_name} in {schema}.{name} result set {index}");
                    }
                    var memberName = column.name?.Replace(' ', '_')?.TrimStart('$');
                    var line = $"public virtual {typeName} {memberName} {{ get; set; }}";
                    if (string.IsNullOrEmpty(memberName))
                    {
                        line = "// " + line;
                        WriteLine(3, writer, $"{ResultErrorPragma} Missing column name. Column index: {column.column_ordinal} in {schema}.{name} result set {index}");
                    }
                    else if (!names.Add(memberName))
                    {
                        line = "// " + line;
                        WriteLine(3, writer, $"{ResultErrorPragma} Duplicate column name: {memberName}, column index: {column.column_ordinal}, {schema}.{name} result set {index}");
                    }
                    WriteLine(3, writer, line);
                }
            });

            return base.VisitResultSetAsync(schema, name, index, columns);
        }

        protected void WriteResultSet(string schema, string name, int index, Action<TextWriter> writeResultBody)
        {
            var dir = new DirectoryInfo(Path.Combine(resultTypesDir.FullName, schema));
            dir.Create();

            if (!resultSetClassFiles.TryGetValue((schema, name), out var classWriter))
            {
                classWriter = CreateTextFile(dir, $"{name}.generated.cs");
                resultSetClassFiles[(schema, name)] = classWriter;

                BeginCodeFile(dir, classWriter, $"public static partial class {name}");
                codeFiles.Add(classWriter);
            }

            // 1-based index for consistency Results<>.Set1, Results<>.Set2, etc.
            // Using a type named ...Result0 from a property named Set1 would be confusing.
            var resultTypeName = $"Result{index + 1}";
            resultTypes.Add($"{GetRelativeNamespace(dir)}.{name}.{resultTypeName}");
            WriteLine(2, classWriter, $"public partial class {resultTypeName}");
            WriteLine(2, classWriter, "{");
            writeResultBody.Invoke(classWriter);
            WriteLine(2, classWriter, "}");
        }

        protected override Task VisitDescribeResultSetExceptionAsync(string schema, string name, SqlException error)
        {
            resultTypeError = true;
            WriteResultSet(schema, name, 0, writer =>
            {
                WriteLine(3, writer, "/*");

                    // Mimic SqlException.ToString() without the stack trace or conneciton id
                    writer.WriteLine($"{error.GetType().FullName} (0x{error.ErrorCode:X}): {error.Message}");
                writer.WriteLine($"Error Number:{error.Number},State:{error.State},Class:{error.Class}");

                WriteLine(3, writer, "*/");
                WriteLine(3, writer, $"{ResultErrorPragma} Failed to describe stored procedure result set(s).");
            });

            return base.VisitDescribeResultSetExceptionAsync(schema, name, error);
        }

        protected override async Task<string> GetSampleResultSetScriptAsync(string schema, string name)
        {
            var scriptFile = new FileInfo(Path.Combine(root.FullName, "SampleScripts", $"{schema}.{name}.sql"));
            string script = null;
            if (scriptFile.Exists)
            {
                using var reader = scriptFile.OpenText();
                script = await reader.ReadToEndAsync();
            }
            return script;
        }

        private string ResultErrorPragma => $"#{(ErrorOnDescribeResultFail ? "error" : "warning")}";

        private string GetRelativeNamespace(DirectoryInfo nested)
        {
            var ns = nested.FullName.Substring(root.FullName.Length + 1).Replace(Path.DirectorySeparatorChar, '.');

            // prefix C# keywords with `@`
            // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/
            ns = Regex.Replace(ns, @"\b((ref)|(public)|(default))\b", "@$1");
            return ns;
        }

        private static DirectoryInfo CreateDirectory(DirectoryInfo dir, string name)
        {
            var newDir = new DirectoryInfo(Path.Combine(dir.FullName, name));
            if (newDir.Exists)
            {
                foreach (var oldFile in newDir.EnumerateFiles("*.generated.cs", SearchOption.AllDirectories))
                {
                    oldFile.Delete();
                }
            }
            newDir.Create();
            return newDir;
        }

        private static StreamWriter CreateTextFile(DirectoryInfo parent, string filename)
        {
            return File.CreateText(Path.Combine(parent.FullName, filename));
        }

        private static void WriteLine(int indent, TextWriter writer, string value)
        {
            for (int i = 0; i < indent * 4; i++)
            {
                writer.Write(' ');
            }
            writer.WriteLine(value);
        }

        private static string ToCamelCase(string name)
        {
            if (name.Length > 0)
            {
                name = name.Substring(0, 1).ToLowerInvariant() + name.Substring(1);
            }
            return name;
        }

        private static string ToPascalCase(string name)
        {
            if (name.Length > 0)
            {
                name = name.Substring(0, 1).ToUpperInvariant() + name.Substring(1);
            }
            return name;
        }

        private static string GetParameterName(string name)
        {
            name = ToCamelCase(name.TrimStart('@'));

            // FIXME check for reserved names
            return name;
        }
    }

}
