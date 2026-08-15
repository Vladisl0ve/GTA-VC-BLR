using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class DocumentWorkflowTests
{
    [TestMethod]
    public void CreateSnapshot_IsDetachedFromLiveProject()
    {
        var factory = new GxtManagerFactory();
        var manager = GxtManagerFactory.Create(
            GXTType.GtaIII,
            sourceName: "main.gxt",
            sourceTexts: ["Original"],
            language: GxtLanguage.English);
        manager.AddGXTEntry("HELLO", "Original");
        var project = new EditorProject
        {
            GxtSourceName = "main.gxt",
            GxtSourcePath = "main.gxt",
            GameType = GXTType.GtaIII,
            GxtManager = manager,
            AttachedTxd = new TxdAttachment
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "fonts.txd",
                DisplayName = "fonts",
                Data = [1, 2, 3],
                Document = new TxdDocument
                {
                    RenderWareVersion = 0,
                    Textures = [],
                },
            },
            Metadata = new ProjectMetadata
            {
                Entries =
                [
                    new ProjectEntryMetadata
                    {
                        Key = "HELLO",
                        Comment = "Original comment",
                    },
                ],
            },
            IsDirty = true,
        };
        var workflow = new DocumentWorkflow(
            factory,
            new ByxProjectSerializer(factory, new TxdReader()));

        var snapshot = workflow.CreateSnapshot(project);
        project.GxtManager.EditGXTEntry("HELLO", "Changed");
        project.Metadata.Entries[0].Comment = "Changed comment";
        project.AttachedTxd.Data[0] = 9;

        Assert.AreNotSame(project, snapshot);
        Assert.AreNotSame(project.GxtManager, snapshot.GxtManager);
        Assert.AreEqual(
            "Original",
            snapshot.GxtManager.ConvertBytesToText(snapshot.GxtManager.GXTEntries.Single().Value));
        Assert.AreEqual("Original comment", snapshot.Metadata.Entries.Single().Comment);
        Assert.AreEqual((byte)1, snapshot.AttachedTxd!.Data[0]);
        Assert.IsTrue(snapshot.IsDirty);
    }
}
