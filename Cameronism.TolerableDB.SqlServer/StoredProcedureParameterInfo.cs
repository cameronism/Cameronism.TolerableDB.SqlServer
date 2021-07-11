namespace Cameronism.TolerableDB.SqlServer
{
    public class StoredProcedureParameterInfo : IDatabaseType
        {
            public string specific_schema { get; set; }
            public string specific_name { get; set; }
            public string type_name { get; set; }
            public int type_schema_id { get; set; }
            public int object_id { get; set; }
            public string name { get; set; }
            public int parameter_id { get; set; }
            public int system_type_id { get; set; }
            public int user_type_id { get; set; }
            public int max_length { get; set; }
            public int precision { get; set; }
            public int scale { get; set; }
            public bool is_output { get; set; }
            public string is_cursor_ref { get; set; }
            public string has_default_value { get; set; }
            public string is_xml_document { get; set; }
            //public object default_value { get; set; }
            public int xml_collection_id { get; set; }
            public bool is_readonly { get; set; }
            public bool is_nullable { get; set; }
            //public object encryption_type { get; set; }
            //public object encryption_type_desc { get; set; }
            //public object encryption_algorithm_name { get; set; }
            //public object column_encryption_key_id { get; set; }
            //public object column_encryption_key_database_name { get; set; }
        }

}
