using CommandLine;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

namespace Cameronism.TolerableDB.SqlServer
{
    internal static class Program
    {
        private class Options
        {
            [Option('c', Required = true, HelpText = "Connection string of database to introspect")]
            public string ConnectionString { get; set; }

            [Option('d', Required = true, HelpText = "Destination folder for generated files")]
            public DirectoryInfo Directory { get; set; }

            [Option('n', Required = false, HelpText = "Namespace for generated files")]
            public string Namespace { get; set; }

            [Option('e', Required = false, HelpText = "Generate warnings or errors for problems")]
            public bool? GenerateErrors { get; set; }
        }

        private static Task Main(string[] args) => Parser.Default.ParseArguments<Options>(args).WithParsedAsync(Run);

        private async static Task Run(Options options)
        {
            using var conn = new SqlConnection(options.ConnectionString);
            var builder = new InsightInterfaceBuilder(conn, options.Directory);
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                builder.Namespace = options.Namespace;
            }
            if (options.GenerateErrors is bool errors)
            {
                builder.ErrorOnDescribeResultFail = errors;

            }
            await builder.VisitAsync();
        }
    }
}
