namespace ReplayViewer.Tests;

public class CosmeticIdsTests
{
    [Fact]
    public void Extracts_hid_from_hero_type()
    {
        Assert.Equal(
            "HID_001_Athena_Commando_F",
            CosmeticIds.Extract(null, "AthenaHeroItemDefinition'/Game/Athena/Heroes/HID_001_Athena_Commando_F.HID_001_Athena_Commando_F'")
        );
    }

    [Fact]
    public void Hid_lookups_include_default_cid()
    {
        var ids = CosmeticIds.LookupIds("HID_001_Athena_Commando_F");
        Assert.Contains("CID_001_Athena_Commando_F_Default", ids);
        Assert.Contains("CID_001_Athena_Commando_F", ids);
    }
}
