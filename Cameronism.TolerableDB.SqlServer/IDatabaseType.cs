namespace Cameronism.TolerableDB.SqlServer
{
    public interface IDatabaseType
    {
        string type_name { get; }
        bool is_nullable { get; }
        string name { get; }
        int max_length { get; }
    }
}
