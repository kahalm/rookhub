namespace RookHub.Api.Services.ChessBase;

/// <summary>
/// Die Übersetzungstabellen der klassischen Zugkodierung (<c>.cbg</c>): jedes Byte des Zugstroms geht durch die Tabelle
/// seines Kodierungsmodus, verschoben um die Zahl der schon gelesenen Züge. Übernommen aus der Formatbeschreibung in
/// Morphys <c>format/v1/2-moves.md</c> (die Werte sind Fakten des Formats); dort stehen die Tabellen der Modi 0, 4, 5 und
/// 10 — die übrigen Modi sind nicht bekannt, und Partien in ihnen werden übersprungen. Jede Tabelle ist eine Permutation
/// von 0–255 (<c>ChessBaseReaderTests</c> prüft das).
/// </summary>
internal static class CbhTables
{
    public static readonly byte[] Mode0 = Hex(
        "a29543f5c13d4a6c5383cc7cffae68add1928b8d35815e74268eabcafd9af3a0" +
        "a515fcb11eed30ea22eba7cd4e6f2e243294418c6e588250bb028ad8fa60de52" +
        "ba46ac299dd7df08210166a3f11927b591d5420eb44cd9185fbc25a69604566a" +
        "aa331c2b73f0dda437d3c510bf5a2334755bb855d26b093a5712b37748859b0f" +
        "9ec7c8a17f7ac0bd316df63ec31171ce7ddaa85490971f444016c9e32ccb84ec" +
        "9f3f5ce6760b3c20b73600dce7f94ff7af0607e01a0aa94b0cd66387891d131b" +
        "e4700547677b2feee2e8980defcfc4f4fbb0179964f2d42a034d78c6fe658688" +
        "79453be5498f2db9be629314e9d0389cb2c2595db67251f8287e6139e1db6980");

    public static readonly byte[] Mode4 = Hex(
        "58a3ee7247b91fa026fddc6df1e3cbc5e2d029e789e1ae7b3441354ba57cf6e5" +
        "db8c5f3705bb32544cef5d2a7fd38186df18c34416f7850f4d30791445aaa26c" +
        "1592a6624819e93c389428a866090363207d36d72e98ecda01132473cd9ecfd9" +
        "d5688bd2ab5b69b027fc311b537e65765575af0accc7c4b2b522e8a7bef5005a" +
        "21d1748ede9ce4422bb19b93c17870d6b6ad4e6a647a5749119d12c03a2fe684" +
        "c607ce08820490b887ca6e1e401a50b410bc438a710b8d5c3b3f88baf4f3611d" +
        "f9fe25510debc8591746a4f256b7022d339fd883fbbfeaedac606739dd966ba9" +
        "c2770c0e91fa999ae0a1f05ec9064fbd5280d4f897233e1c2c954a8f3d6fffb3");

    public static readonly byte[] Mode5 = Hex(
        "e01b9786a248f0230137faeb50765dcd801a315cffbc78dae5a375b371e1a10e" +
        "56413fe30dc425e6888970dc99a60b9a142c579e697adf55ad737cc50372f545" +
        "628eaf5fccc05ef31813b53809283cbd108be9dd8c940881a7fe2479442bc136" +
        "327f67e4fd494b21a95b771f1cf640653df95af8472653d420d2d84f27aefbb6" +
        "9b74b842fc166fd79fb97b60dec246069cd32ac705853eb10015d196ce909511" +
        "338fc68284b739a0e8341e7da8d0eae2ef4cc3584e021db27e982d07d5ca68cb" +
        "30b4ac8363ec87ee2ebecf6d4d8ad604b035c8c91217618d640fa554293a4a6b" +
        "bb2fd93b936aaaa422f419436ef1f7db92ed66590abf6c519152f20c9dabbae7");

    public static readonly byte[] Mode10 = Hex(
        "385064cc9f566f734d4166a2c6b99647eecfb16d484f442015be82093499eb1a" +
        "337cab59002224b0b4fa85dd57f3d9d28a8d70b56e43a70b2708eab3941c631b" +
        "2bc50cd43f37e8dea623ef06fd3ec14032df2dc8d00ff1e27a95ec3cca4911e0" +
        "25d830fe2ebadcaa534cd15e678b0d9ea9bf057835c4b6a44bd60452a8762cbd" +
        "ae39b2e30381179a3d68c06592c9f77716fff5e11e36bb868ef99dd56bf47480" +
        "2f7b149729e54a02935ccba15879f85f26288f7ea5edd389c7889b548760daf0" +
        "453ac2849021f6f25a2a55186101fbe68c6a12b862839810715be4addb420acd" +
        "46d77207ce19b73b319c51c369a391e90e1ffc756c1d7de7af5da0bc137fac4e");

    private static byte[] Hex(string s) => Convert.FromHexString(s);
}
