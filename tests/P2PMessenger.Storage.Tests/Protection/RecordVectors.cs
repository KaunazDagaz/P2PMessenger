namespace P2PMessenger.Storage.Tests.Protection;

internal static class RecordVectors
{
    public const string Rfc5869Ikm = "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b";
    public const string Rfc5869Salt = "000102030405060708090a0b0c";
    public const string Rfc5869Info = "f0f1f2f3f4f5f6f7f8f9";
    public const string Rfc5869Prk = "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5";
    public const string Rfc5869Okm = "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865";

    public const string XChaChaKey = "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f";
    public const string XChaChaNonce = "404142434445464748494a4b4c4d4e4f5051525354555657";
    public const string XChaChaAssociatedData = "50515253c0c1c2c3c4c5c6c7";
    public const string XChaChaPlaintext = "Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it.";

    public const string XChaChaCiphertext =
        "bd6d179d3e83d43b9576579493c0e939572a1700252bfaccbed2902c21396cbb731c7f1b0b4aa6440bf3a82f4eda7e39ae64c6708c54c216cb96b72e1213b452"
        + "2f8c9ba40db5d945b11b69b982c1bb9e3f3fac2bc369488f76b2383565d3fff921f9664c97637da9768812f615c68b13b52e";

    public const string XChaChaTag = "c0875924c1c7987947deafd8780acf49";

    public const string DataKey = "101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f";

    public const string NameAssociatedData = "5032504d2f73746f726167652f763100706572736f6e00646973706c61795f6e616d655f656e63000000000000000002";

    public const string SealedName = "a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b716024612612d7ba476feccf8e23ae68e4c261f3d7f";

    public const string SealedEmptyText = "a0a1a2a3a4a5a6a7a8a9aaabacadaeafb0b1b2b3b4b5b6b77f48ec992a5c6ee0a0fe00c095eb564d";

    public const string LookupValue = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    public const string IdentityKeyLookup = "29db84479abe593a3a7a8c76f95ea1da70de61b737279576c4115feb1a8796ee";
    public const string StaticKeyLookup = "176a2828ca92eb78b983be5de544d37fff042d16dbc6a41a281afb55f547f20d";
    public const string MessageIdLookup = "caf7d7e2cdaf950df8081ef1e36c397c1fed6cb057061f7862879ea1a261d496";

    public static byte[] Bytes(string hex) => Convert.FromHexString(hex);
}
