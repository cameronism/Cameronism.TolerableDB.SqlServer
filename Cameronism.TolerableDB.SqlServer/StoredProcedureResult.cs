namespace Cameronism.TolerableDB.SqlServer
{
    public class StoredProcedureResult : IDatabaseType
        {
            public bool is_hidden { get; set; }
            public int column_ordinal { get; set; }
            public string name { get; set; }
            public bool is_nullable { get; set; }
            public int system_type_id { get; set; }
            public string system_type_name { get; set; }
            public int max_length { get; set; }
            public int precision { get; set; }
            public int scale { get; set; }
            //public object collation_name { get; set; }
            //public object user_type_id { get; set; }
            //public object user_type_database { get; set; }
            //public object user_type_schema { get; set; }
            //public object user_type_name { get; set; }
            //public object assembly_qualified_type_name { get; set; }
            //public object xml_collection_id { get; set; }
            //public object xml_collection_database { get; set; }
            //public object xml_collection_schema { get; set; }
            //public object xml_collection_name { get; set; }
            public bool is_xml_document { get; set; }
            public bool is_case_sensitive { get; set; }
            public bool is_fixed_length_clr_type { get; set; }
            //public object source_server { get; set; }
            //public object source_database { get; set; }
            //public object source_schema { get; set; }
            //public object source_table { get; set; }
            //public object source_column { get; set; }
            public bool is_identity_column { get; set; }
            //public object is_part_of_unique_key { get; set; }
            public bool is_updateable { get; set; }
            public bool is_computed_column { get; set; }
            public bool is_sparse_column_set { get; set; }
            //public object ordinal_in_order_by_list { get; set; }
            //public object order_by_is_descending { get; set; }
            public int order_by_list_length { get; set; }
            public int tds_type_id { get; set; }
            public int tds_length { get; set; }

            string IDatabaseType.type_name
            {
                get
                {
                    // strip off the length info if any
                    var name = system_type_name;
                    var paren = name.IndexOf('(');
                    if (paren > 0)
                    {
                        name = name.Substring(0, paren);
                    }
                    return name;
                }
            }

            //public object tds_collation_id { get; set; }
            //public object tds_collation_sort_id { get; set; }
        }

}
