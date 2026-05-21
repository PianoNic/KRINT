namespace KRINT.Domain
{
    public class Secret : BaseEntity
    {
        public required string Name { get; init; }
        public required byte[] Ciphertext { get; set; }
        public required byte[] Nonce { get; set; }
        public required byte[] Tag { get; set; }
    }
}
