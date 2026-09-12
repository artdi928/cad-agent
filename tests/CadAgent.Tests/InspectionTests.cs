using System.Text.Json;
using CadAgent.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CadAgent.Tests;

[TestClass]
public sealed class InspectionTests
{
    [TestMethod]
    public void DBTextDto_SerializesCorrectly()
    {
        var dto = new EntityDto(
            Handle: "1A",
            EntityType: "DBText",
            Layer: "TextLayer",
            Space: "model",
            Layout: "Model",
            Position: new PositionDto(10.5, 20.0, 0.0),
            Text: "Sample text with Unicode: Привет Мир");

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"entityType\":\"DBText\""));
        Assert.IsTrue(json.Contains("\"text\":\"Sample text with Unicode: Привет Мир\""));
        Assert.IsTrue(json.Contains("\"position\":{\"x\":10.5,\"y\":20,\"z\":0}"));
        Assert.IsFalse(json.Contains("\"plainText\""));
        Assert.IsFalse(json.Contains("\"rawContents\""));
    }

    [TestMethod]
    public void MTextDto_SeparatesPlainTextAndRawContents()
    {
        var raw = @"{\fArial|b1|i0|c204|p34;Sample Bold Text}";
        var plain = "Sample Bold Text";

        var dto = new EntityDto(
            Handle: "2B",
            EntityType: "MText",
            Layer: "Annotations",
            Space: "paper",
            Layout: "Layout1",
            Location: new PositionDto(100.0, 200.0, 0.0),
            PlainText: plain,
            RawContents: raw);

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"plainText\":\"Sample Bold Text\""));
        Assert.IsTrue(json.Contains("\"rawContents\":"));

        var roundTripped = JsonSerializer.Deserialize<EntityDto>(json, JsonDefaults.Options);
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(plain, roundTripped.PlainText);
        Assert.AreEqual(raw, roundTripped.RawContents);
    }

    [TestMethod]
    public void MLeaderDto_TextBacked_SerializesAnchorAndText()
    {
        var dto = new EntityDto(
            Handle: "3C",
            EntityType: "MLeader",
            Layer: "Leaders",
            Space: "model",
            Layout: "Model",
            Anchor: new PositionDto(50.0, 50.0, 0.0),
            PlainText: "Leader Plain Text",
            RawContents: "{\\C1;Leader Plain Text}",
            ContentType: "MTextContent");

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"contentType\":\"MTextContent\""));
        Assert.IsTrue(json.Contains("\"plainText\":\"Leader Plain Text\""));
        Assert.IsTrue(json.Contains("\"anchor\":{\"x\":50,\"y\":50,\"z\":0}"));
    }

    [TestMethod]
    public void MLeaderDto_NonTextBacked_HasNullTextAndActualContentType()
    {
        var dto = new EntityDto(
            Handle: "4D",
            EntityType: "MLeader",
            Layer: "Leaders",
            Space: "model",
            Layout: "Model",
            ContentType: "BlockContent");

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"contentType\":\"BlockContent\""));
        Assert.IsFalse(json.Contains("\"plainText\""));
        Assert.IsFalse(json.Contains("\"rawContents\""));
        Assert.IsFalse(json.Contains("\"anchor\""));
    }

    [TestMethod]
    public void BlockReferenceDto_SerializesAttributesDeterministically()
    {
        var attrs = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["TAG_B"] = "Value B",
            ["TAG_A"] = "Value A"
        };

        var dto = new EntityDto(
            Handle: "5E",
            EntityType: "BlockReference",
            Layer: "0",
            Space: "model",
            Layout: "Model",
            Position: new PositionDto(0, 0, 0),
            EffectiveName: "VALVE_BLOCK",
            Attributes: attrs);

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"effectiveName\":\"VALVE_BLOCK\""));
        Assert.IsTrue(json.Contains("\"attributes\":{\"TAG_A\":\"Value A\",\"TAG_B\":\"Value B\"}"));
    }

    [TestMethod]
    public void EntityDto_DoesNotContainObjectIdProperty()
    {
        var properties = typeof(EntityDto).GetProperties();
        foreach (var prop in properties)
        {
            Assert.IsFalse(prop.Name.Contains("ObjectId", StringComparison.OrdinalIgnoreCase),
                $"EntityDto must not expose ObjectId: found property '{prop.Name}'.");
            Assert.IsFalse(prop.PropertyType.Name.Contains("ObjectId", StringComparison.OrdinalIgnoreCase),
                $"EntityDto property '{prop.Name}' has type '{prop.PropertyType.Name}' referencing ObjectId.");
        }
    }
}
