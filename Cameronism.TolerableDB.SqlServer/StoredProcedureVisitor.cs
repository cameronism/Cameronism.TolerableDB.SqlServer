using Insight.Database;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace Cameronism.TolerableDB.SqlServer
{
    public abstract class StoredProcedureVisitor
    {
        private IDbConnection connection;

        public StoredProcedureVisitor(IDbConnection connection)
        {
            this.connection = connection;
        }

        protected virtual Task VisitAllParameters(IList<StoredProcedureParameterInfo> parameters) => Task.CompletedTask;

        protected virtual Task<string> GetSampleResultSetScriptAsync(string schema, string name) => Task.FromResult<string>(null);

        protected virtual Task VisitResultSetAsync(string schema, string name, int index, IList<StoredProcedureResult> columns) => Task.CompletedTask;

        protected virtual Task VisitParameterAsync(StoredProcedureParameterInfo parameter) => Task.CompletedTask;

        protected virtual Task VisitDescribeResultSetExceptionAsync(string schema, string name, SqlException error) => Task.CompletedTask;

        public virtual async Task VisitAsync()
        {
            var parameters = await connection.QuerySqlAsync<StoredProcedureParameterInfo>(@"
            select
                r.specific_schema,
                r.specific_name,
                t.name as type_name,
                t.schema_id as type_schema_id,
                p.*
            from information_schema.routines r
            join sys.parameters p on p.object_id = OBJECT_ID(r.specific_schema + '.' + r.specific_name)
            join sys.types t on t.system_type_id = p.system_type_id and t.user_type_id = p.user_type_id
            where r.routine_type = 'PROCEDURE'
            order by p.object_id, p.parameter_id
            ");

            await VisitAllParameters(parameters);

            foreach (var group in parameters.GroupBy(p => p.specific_schema))
            {
                await VisitSchemaAsync(group.Key, group);
            }
        }

        protected async virtual Task VisitSchemaAsync(string schema, IEnumerable<StoredProcedureParameterInfo> parameters)
        {
            foreach (var proc in parameters.OrderBy(p => p.specific_name).GroupBy(p => p.object_id))
            {
                var first = proc.First();
                await VisitProcedureAsync(first.specific_schema, first.specific_name, proc);
            }
        }

        protected async virtual Task VisitProcedureAsync(string schema, string name, IEnumerable<StoredProcedureParameterInfo> parameters)
        {
            foreach (var param in parameters.OrderBy(p => p.parameter_id))
            {
                await VisitParameterAsync(param);
            }

            int index = 0;
            foreach (var columns in await GetAllResultSetColumnsAsync(schema, name))
            {
                await VisitResultSetAsync(schema, name, index, columns);
                index++;
            }
        }

        /// <summary>
        /// Sample scripts should be considered first choice. sp_describe_first_result_set will be used as a fallback but it is not perfect
        /// </summary>
        protected async virtual Task<IEnumerable<IList<StoredProcedureResult>>> GetAllResultSetColumnsAsync(string schema, string name)
        {
            string sampleScript = await GetSampleResultSetScriptAsync(schema, name);
            if (!string.IsNullOrWhiteSpace(sampleScript))
            {
                var results = await ExecuteSampleResultSetScriptAsync(schema, name, sampleScript);
                if (results != null)
                {
                    return results;
                }
            }

            var (columns, error) = await DescribeFirstResultSetAsync(schema, name);
            if (columns?.Count > 0)
            {
                return new[] { columns };
            }

            if (error != null)
            {
                await VisitDescribeResultSetExceptionAsync(schema, name, error);
            }
            return Enumerable.Empty<IList<StoredProcedureResult>>();
        }

        /// <summary>
        /// Execute script in a transaction and then roll back the transaction
        /// </summary>
        protected virtual Task<IEnumerable<IList<StoredProcedureResult>>> ExecuteSampleResultSetScriptAsync(string schema, string name, string sampleScript)
        {
            var results = new List<IList<StoredProcedureResult>>();
            using var transaction = connection.OpenWithTransaction();
            using var reader = connection.GetReaderSql(sampleScript, transaction: transaction);
            do
            {
                results.Add(DescribeReader(reader));
            }
            while (reader.NextResult());
            transaction.Rollback();
            return Task.FromResult<IEnumerable<IList<StoredProcedureResult>>>(results);
        }

        protected virtual async Task<(IList<StoredProcedureResult> columns, SqlException error)> DescribeFirstResultSetAsync(string schema, string name)
        {
            IList<StoredProcedureResult> columns = null;
            SqlException error = null;
            try
            {
                columns = await connection.QuerySqlAsync<StoredProcedureResult>("exec sp_describe_first_result_set @tsql", new { tsql = $"{schema}.{name}" });
            }
            catch (SqlException se)
            {
                error = se;
            }
            return (columns, error);
        }

        protected virtual Task<IList<TableTypeColumn>> GetTableTypeColumnsAsync(StoredProcedureParameterInfo parameter)
        {
            return connection.QuerySqlAsync<TableTypeColumn>(@"
                select
                    tt.name as table_name,
                    st.name as type_name,
                    cc.*
                from sys.columns cc
                join sys.table_types tt on tt.type_table_object_id = cc.object_id
                join sys.types st on st.system_type_id = cc.system_type_id and st.user_type_id = cc.user_type_id
                where tt.system_type_id = @system_type_id and tt.user_type_id = @user_type_id
            ", new { parameter.system_type_id, parameter.user_type_id });
        }

        private static IList<StoredProcedureResult> DescribeReader(IDataReader reader)
        {
            var columns = new List<StoredProcedureResult>(reader.FieldCount);
            using var schemaTable = reader.GetSchemaTable();
            foreach (DataRow column in schemaTable.Rows)
            {
                // https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqldatareader.getschematable?view=netframework-4.7.2
                columns.Add(new StoredProcedureResult
                {
                    column_ordinal = column.Field<int>("ColumnOrdinal"),
                    name = column.Field<string>("ColumnName"),
                    is_nullable = column.Field<bool>("AllowDBNull"),
                    max_length = column.Field<int>("ColumnSize"),
                    system_type_name = column.Field<string>("DataTypeName"),
                });
            }

            while (reader.Read())
            {
                // do nothing
                // just consume all rows
            }

            return columns;
        }

        protected static bool GetStandardTypeName(IDatabaseType param, out string typeName)
        {
            // see https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings


            // reference types
            typeName = param.type_name switch
            {
                "nvarchar" or "varchar" => nameof(String),
                "char" when param.max_length > 1 => nameof(String),
                "binary" or "varbinary" => "byte[]",

                "sql_variant" => nameof(Object),

                // weird strings
                "sysname" => nameof(String),

                // Is there a better default?
                // https://docs.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.types.sqlhierarchyid?view=sql-dacfx-150
                "hierarchyid" => nameof(String),


                _ => null,
            };

            if (typeName != null)
            {
                return true;
            }

            // value types
            typeName = param.type_name switch
            {
                "bit" => nameof(Boolean),
                "tinyint" => nameof(Byte),
                "smallint" => nameof(Int16),
                "int" => nameof(Int32),
                "bigint" => nameof(Int64),

                "real" or "float" => nameof(Double),
                "decimal" or "numeric" => nameof(Decimal),

                "date" or "datetime" or "datetime2" or "smalldatetime" => nameof(DateTime),
                "time" => nameof(TimeSpan),

                "datetimeoffset" => nameof(DateTimeOffset),
                "uniqueidentifier" => nameof(Guid),
                "char" => nameof(Char),
                _ => null,
            };

            if (typeName != null && param.is_nullable)
            {
                typeName += "?";
            }

            return typeName != null;
        }
    }
}
