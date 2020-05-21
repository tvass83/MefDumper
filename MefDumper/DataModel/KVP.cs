namespace MefDumper.DataModel
{
    public struct KVP
    {
        public KVP(ulong key, ulong value)
        {
            this.key = key;
            this.value = value;
        }

        public ulong key;
        public ulong value;
    }
}
