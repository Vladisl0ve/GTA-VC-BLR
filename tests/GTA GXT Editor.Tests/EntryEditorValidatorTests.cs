using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class EntryEditorValidatorTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("TOO_LONG_9")]
    [DataRow("КЛЮЧ")]
    public void InvalidKeys_UseSharedDomainValidation(string key)
    {
        var result = EntryEditorValidator.Validate(CreateRequest(), key, "Text", null, null);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(EntryEditorField.Name, result.ErrorField);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("TOO_LONG_9")]
    [DataRow("ТАБЛИЦА")]
    public void InvalidTables_UseSharedDomainValidation(string table)
    {
        var request = CreateRequest([new TableOption("MAIN", "MAIN")]);

        var result = EntryEditorValidator.Validate(request, "HELLO", "Text", table, null);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(EntryEditorField.Table, result.ErrorField);
    }

    [TestMethod]
    public void ValidEntry_IsNormalizedIntoResult()
    {
        var result = EntryEditorValidator.Validate(
            CreateRequest([new TableOption("MAIN", "MAIN")]),
            "  HELLO  ",
            "Text",
            "MAIN",
            "   ");

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("HELLO", result.Result!.Name);
        Assert.AreEqual("MAIN", result.Result.RawTableName);
        Assert.IsNull(result.Result.Comment);
    }

    private static EntryEditorRequest CreateRequest(IReadOnlyList<TableOption>? tables = null) =>
        new(true, string.Empty, string.Empty, null, tables ?? []);
}
