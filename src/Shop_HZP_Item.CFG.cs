namespace Shop_HZP_Item;

using ShopCore.Contract;

public class ShopHZPItemCFG
{
    public ZombieModuleSettings Settings { get; set; } = new();
    public List<ZombieItemTemplate> Items { get; set; } = [];
}

public class ZombieModuleSettings
{
    public bool UseCorePrefix { get; set; } = true;
    public string Category { get; set; } = "Zombie Items";
    public float GodModeDuration { get; set; } = 20f;
    public float InfiniteAmmoDuration { get; set; } = 20f;
}

public class ZombieItemTemplate
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DisplayNameKey { get; set; } = string.Empty;
    public int Price { get; set; } = 0;
    public int? SellPrice { get; set; }
    public int DurationSeconds { get; set; } = 0;
    public int HealthAmount { get; set; } = 200;
    public string Type { get; set; } = nameof(ShopItemType.Consumable);
    public string Team { get; set; } = nameof(ShopItemTeam.Any);
    public bool Enabled { get; set; } = true;
    public bool CanBeSold { get; set; } = false;
}