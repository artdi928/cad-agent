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

    [TestMethod]
    public void LineDto_SerializesStartAndEnd()
    {
        var dto = new EntityDto(
            Handle: "6F",
            EntityType: "Line",
            Layer: "0",
            Space: "model",
            Layout: "Model",
            Start: new PositionDto(0, 0, 0),
            End: new PositionDto(1000, 500, 0));

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"entityType\":\"Line\""));
        Assert.IsTrue(json.Contains("\"start\":{\"x\":0,\"y\":0,\"z\":0}"));
        Assert.IsTrue(json.Contains("\"end\":{\"x\":1000,\"y\":500,\"z\":0}"));

        var roundTripped = JsonSerializer.Deserialize<EntityDto>(json, JsonDefaults.Options);
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(0.0, roundTripped.Start!.X);
        Assert.AreEqual(1000.0, roundTripped.End!.X);
    }

    [TestMethod]
    public void PolylineDto_SerializesVerticesAndClosed()
    {
        var vertices = new List<PositionDto>
        {
            new(0, 0, 0),
            new(1000, 0, 0),
            new(1000, 500, 0),
            new(0, 500, 0)
        };

        var dto = new EntityDto(
            Handle: "7A",
            EntityType: "Polyline",
            Layer: "0",
            Space: "model",
            Layout: "Model",
            Vertices: vertices,
            Closed: true);

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"entityType\":\"Polyline\""));
        Assert.IsTrue(json.Contains("\"closed\":true"));
        Assert.IsTrue(json.Contains("\"vertices\":["));

        var roundTripped = JsonSerializer.Deserialize<EntityDto>(json, JsonDefaults.Options);
        Assert.IsNotNull(roundTripped);
        Assert.IsTrue(roundTripped.Closed == true);
        Assert.AreEqual(4, roundTripped.Vertices!.Count);
    }

    [TestMethod]
    public void CircleDto_SerializesCenterAndRadius()
    {
        var dto = new EntityDto(
            Handle: "8B",
            EntityType: "Circle",
            Layer: "0",
            Space: "model",
            Layout: "Model",
            Center: new PositionDto(2500, 250, 0),
            Radius: 500.0);

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"entityType\":\"Circle\""));
        Assert.IsTrue(json.Contains("\"center\":{\"x\":2500,\"y\":250,\"z\":0}"));
        Assert.IsTrue(json.Contains("\"radius\":500"));

        var roundTripped = JsonSerializer.Deserialize<EntityDto>(json, JsonDefaults.Options);
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(500.0, roundTripped.Radius);
        Assert.AreEqual(2500.0, roundTripped.Center!.X);
    }

    [TestMethod]
    public void ArcDto_SerializesCenterRadiusAndAngles()
    {
        var dto = new EntityDto(
            Handle: "9C",
            EntityType: "Arc",
            Layer: "0",
            Space: "model",
            Layout: "Model",
            Center: new PositionDto(0, 0, 0),
            Radius: 300.0,
            StartAngle: 0.0,
            EndAngle: Math.PI);

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"entityType\":\"Arc\""));
        Assert.IsTrue(json.Contains("\"radius\":300"));
        Assert.IsTrue(json.Contains("\"startAngle\":0"));

        var roundTripped = JsonSerializer.Deserialize<EntityDto>(json, JsonDefaults.Options);
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(300.0, roundTripped.Radius);
        Assert.AreEqual(Math.PI, roundTripped.EndAngle!.Value, 1e-6);
    }

    [TestMethod]
    public void DBTextDto_WithAlignment_SerializesAnchorHeightRotation()
    {
        var dto = new EntityDto(
            Handle: "10D",
            EntityType: "DBText",
            Layer: "0",
            Space: "model",
            Layout: "Model",
            Position: new PositionDto(2500, 250, 0),
            Anchor: new PositionDto(2500, 250, 0),
            Height: 200.0,
            Rotation: 0.0,
            Alignment: "TextCenter/TextVerticalMid",
            Text: "КРУГ");

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Options);
        Assert.IsTrue(json.Contains("\"alignment\":\"TextCenter/TextVerticalMid\""));
        Assert.IsTrue(json.Contains("\"height\":200"));
        Assert.IsTrue(json.Contains("\"anchor\":{\"x\":2500,\"y\":250,\"z\":0}"));
    }
}
