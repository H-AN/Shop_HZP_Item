using Microsoft.Extensions.Logging;
using ShopCore.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;
using HanZombiePlagueS2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Shop_HZP_Item;

[PluginMetadata(
    Id = "Shop_HZP_Item",
    Name = "Shop HZP Item",
    Author = "H-AN",
    Version = "1.0.0",
    Description = "ShopCore module with HZP Items."
)]

public class Shop_HZP_Item (ISwiftlyCore core) : BasePlugin(core)
{
    private ServiceProvider? ServiceProvider { get; set; }
    private const string ModulePluginId = "Shop_HZP_Item";
    private const string TemplateSectionName = "Main";
    private const string DefaultCategory = "Zombie Items";
    public static IShopCoreApiV2? _shopApi { get; private set; }
    public static IHanZombiePlagueAPI? _zpApi { get; private set; }
    private const string ShopCoreInterfaceKey = "ShopCore.API.v2";
    private const string HanZombiePlagueKey = "HanZombiePlague";
    private bool handlersRegistered;
    private readonly HashSet<string> registeredItemIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ZombieItemData> itemDataMap = new(StringComparer.OrdinalIgnoreCase);
    private ZombieModuleSettings runtimeSettings = new();
    
    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (!interfaceManager.HasSharedInterface(HanZombiePlagueKey))
        {
            throw new Exception($"[Shop_HZP_Item] 缺少依赖 {HanZombiePlagueKey} / Missing dependency: {HanZombiePlagueKey}");
        }
        _zpApi = interfaceManager.GetSharedInterface<IHanZombiePlagueAPI>(HanZombiePlagueKey);

        if (_zpApi == null)
        {
            throw new Exception($"[Shop_HZP_Item] 读取 {HanZombiePlagueKey} API 失败 / Failed to load {HanZombiePlagueKey} API");
        }

        if (!interfaceManager.HasSharedInterface(ShopCoreInterfaceKey))
        {
            throw new Exception($"[Shop_HZP_Item] 缺少依赖 {ShopCoreInterfaceKey} / Missing dependency: {ShopCoreInterfaceKey}");
        }

        _shopApi = interfaceManager.GetSharedInterface<IShopCoreApiV2>(ShopCoreInterfaceKey);

        if (_shopApi == null)
        {
            throw new Exception($"[Shop_HZP_Item] 读取 {ShopCoreInterfaceKey} API 失败 / Failed to load {ShopCoreInterfaceKey} API");
        }
    }

    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        if (_shopApi is null)
        {
            Core.Logger.LogWarning("ShopCore API is not available. Zombie items will not be registered.");
            return;
        }

        RegisterItemsAndHandlers();
    }
    
    public override void Load(bool hotReload)
    {
        var collection = new ServiceCollection();
        collection.AddSwiftly(Core);

        collection.AddSingleton<ShopHZPItemGlobals>();

        ServiceProvider = collection.BuildServiceProvider();
    }

    public override void Unload()
    {
        UnregisterItemsAndHandlers();
        ServiceProvider!.Dispose();
    }

    private void RegisterItemsAndHandlers()
    {
        if (_shopApi is null) return;

        UnregisterItemsAndHandlers();

        var moduleConfig = _shopApi.LoadModuleConfig<ShopHZPItemCFG>(
            ModulePluginId,
            ShopHZPItemGlobals.TemplateFileName,
            TemplateSectionName
        );

        NormalizeConfig(moduleConfig);
        runtimeSettings = moduleConfig.Settings;

        var category = string.IsNullOrWhiteSpace(moduleConfig.Settings.Category)
            ? DefaultCategory
            : moduleConfig.Settings.Category.Trim();

        if (moduleConfig.Items.Count == 0)
        {
            moduleConfig = CreateDefaultConfig();
            category = moduleConfig.Settings.Category;
            runtimeSettings = moduleConfig.Settings;
            _ = _shopApi.SaveModuleConfig(
                ModulePluginId,
                moduleConfig,
                ShopHZPItemGlobals.TemplateFileName,
                TemplateSectionName,
                overwrite: true
            );
        }

        var registeredCount = 0;
        foreach (var itemTemplate in moduleConfig.Items)
        {
            if (!TryCreateDefinition(itemTemplate, category, out var definition, out var itemId))
            {
                continue;
            }

            if (!_shopApi.RegisterItem(definition))
            {
                Core.Logger.LogWarning("Failed to register zombie item '{ItemId}'.", definition.Id);
                continue;
            }

            _ = registeredItemIds.Add(definition.Id);
            itemDataMap[definition.Id] = new ZombieItemData
            {
                ItemId = itemId,
                HealthAmount = itemTemplate.HealthAmount
            };

            registeredCount++;
        }

        _shopApi.OnItemPurchased += OnItemPurchased;
        _shopApi.OnItemPreview += OnItemPreview;
        handlersRegistered = true;

        Core.Logger.LogInformation(
            "Shop_HZP_Item initialized. RegisteredItems={RegisteredCount}, Category='{Category}'",
            registeredCount,
            category
        );
    }

    private void UnregisterItemsAndHandlers()
    {
        if (!handlersRegistered || _shopApi is null) return;

        _shopApi.OnItemPurchased -= OnItemPurchased;
        _shopApi.OnItemPreview -= OnItemPreview;
        
        foreach (var itemId in registeredItemIds)
        {
            _ = _shopApi.UnregisterItem(itemId);
        }

        registeredItemIds.Clear();
        itemDataMap.Clear();
        handlersRegistered = false;
    }

    private void OnItemPurchased(IPlayer player, ShopItemDefinition item)
    {
        if (_shopApi is null || !player.IsValid || player.IsFakeClient)
        {
            return;
        }

        if (!itemDataMap.TryGetValue(item.Id, out var itemData))
        {
            return;
        }

        GiveZombieItem(player, itemData);
    }

    private void OnItemPreview(IPlayer player, ShopItemDefinition item)
    {
        if (!registeredItemIds.Contains(item.Id))
        {
            return;
        }

        var displayName = _shopApi?.GetItemDisplayName(player, item) ?? item.DisplayName;
        player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["preview.cannot_preview"]);
    }

    private void GiveZombieItem(IPlayer player, ZombieItemData itemData)
    {
        if (_zpApi is null || !player.IsValid || !player.IsAlive)
        {
            return;
        }

        Core.Scheduler.NextWorldUpdate(() =>
        {
            try
            {
                if (!player.IsValid || !player.IsAlive)
                {
                    return;
                }

                switch (itemData.ItemId.ToLower())
                {
                    case "t_virus_serum":
                        if (!_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_zombie_can_buy"]);
                            return;
                        }
                        _zpApi.HZP_SetTargetTVaccine(player);
                        break;
                    case "t_virus_reagent":
                        if (_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.already_is_zombie"]);
                            return;
                        }
                        _zpApi.HZP_SetTargetZombie(player);
                        break;
                    case "infection_grenade":
                        if (!_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_zombie_can_buy"]);
                            return;
                        }
                        _zpApi.HZP_GiveTVirusGrenade(player);
                        break;
                    case "scba_suit":
                        if (_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_human_can_buy"]);
                            return;
                        }
                        if (_zpApi.HZP_PlayerHaveScbaSuit(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.already_have_scba_suit"]);
                            return;
                        }
                        _zpApi.HZP_GiveScbaSuit(player);
                        break;
                    case "god_mode":
                        if (_zpApi.HZP_PlayerHaveGodState(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.already_have_god_mode"]);
                            return;
                        }
                        _zpApi.HZP_GiveGodState(player, runtimeSettings.GodModeDuration);
                        break;
                    case "add_health":
                        _zpApi.HZP_HumanAddHealth(player, itemData.HealthAmount);
                        break;
                    case "infinite_ammo":
                        if (_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_human_can_buy"]);
                            return;
                        }
                        if (_zpApi.HZP_PlayerHaveInfiniteAmmoState(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.already_have_infinite_ammo"]);
                            return;
                        }
                        _zpApi.HZP_GiveInfiniteAmmo(player, runtimeSettings.InfiniteAmmoDuration);
                        break;
                    case "fire_grenade":
                        if (_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_human_can_buy"]);
                            return;
                        }
                        _zpApi.HZP_GiveFireGrenade(player);
                        break;
                    case "light_grenade":
                        if (_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_human_can_buy"]);
                            return;
                        }
                        _zpApi.HZP_GiveLightGrenade(player);
                        break;
                    case "freeze_grenade":
                        if (_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_human_can_buy"]);
                            return;
                        }
                        _zpApi.HZP_GiveFreezeGrenade(player);
                        break;
                    case "teleport_grenade":
                        if (_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_human_can_buy"]);
                            return;
                        }
                        _zpApi.HZP_GiveTeleportGrenade(player);
                        break;
                    case "incendiary_grenade":
                        if (_zpApi.HZP_IsZombie(player.PlayerID))
                        {
                            player.SendMessage(MessageType.Chat, Core.Translation.GetPlayerLocalizer(player)["player.only_human_can_buy"]);
                            return;
                        }
                        _zpApi.HZP_GiveIncGrenade(player);
                        break;
                    default:
                        Core.Logger.LogWarning("Unknown zombie item '{ItemId}' for player {PlayerId}.", itemData.ItemId, player.PlayerID);
                        break;
                }
            }
            catch (Exception ex)
            {
                Core.Logger.LogWarning(ex, "Failed to give zombie item '{ItemId}' to player {PlayerId}.", itemData.ItemId, player.PlayerID);
            }
        });
    }

    private bool TryCreateDefinition(
        ZombieItemTemplate itemTemplate,
        string category,
        out ShopItemDefinition definition,
        out string itemId)
    {
        definition = default!;
        itemId = default!;

        if (string.IsNullOrWhiteSpace(itemTemplate.Id))
        {
            return false;
        }

        itemId = itemTemplate.Id.Trim();

        if (itemTemplate.Price <= 0)
        {
            Core.Logger.LogWarning("Skipping item '{ItemId}' because Price must be greater than 0.", itemId);
            return false;
        }

        if (!Enum.TryParse(itemTemplate.Type, ignoreCase: true, out ShopItemType itemType))
        {
            Core.Logger.LogWarning("Skipping item '{ItemId}' because Type '{Type}' is invalid.", itemId, itemTemplate.Type);
            return false;
        }

        if (!Enum.TryParse(itemTemplate.Team, ignoreCase: true, out ShopItemTeam team))
        {
            team = ShopItemTeam.Any;
        }

        decimal? sellPrice = null;
        if (itemTemplate.SellPrice.HasValue && itemTemplate.SellPrice.Value >= 0)
        {
            sellPrice = itemTemplate.SellPrice.Value;
        }

        TimeSpan? duration = null;
        if (itemTemplate.DurationSeconds > 0)
        {
            duration = TimeSpan.FromSeconds(itemTemplate.DurationSeconds);
        }

        definition = new ShopItemDefinition(
            Id: itemId,
            DisplayName: ResolveDisplayName(itemTemplate),
            Category: category,
            Price: itemTemplate.Price,
            SellPrice: sellPrice,
            Duration: duration,
            Type: itemType,
            Team: team,
            Enabled: itemTemplate.Enabled,
            CanBeSold: itemTemplate.CanBeSold
        );
        return true;
    }

    private string ResolveDisplayName(ZombieItemTemplate itemTemplate, IPlayer? player = null)
    {
        if (!string.IsNullOrWhiteSpace(itemTemplate.DisplayNameKey))
        {
            var key = itemTemplate.DisplayNameKey.Trim();
            
            var localizer = player is null ? Core.Localizer : Core.Translation.GetPlayerLocalizer(player);
            var localized = localizer[key];
            if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
            {
                return localized;
            }
        }

        if (!string.IsNullOrWhiteSpace(itemTemplate.DisplayName))
        {
            return itemTemplate.DisplayName.Trim();
        }

        return itemTemplate.Id.Trim();
    }

    private static void NormalizeConfig(ShopHZPItemCFG config)
    {
        config.Settings ??= new ZombieModuleSettings();
        config.Items ??= [];
    }

    private static ShopHZPItemCFG CreateDefaultConfig()
    {
        return new ShopHZPItemCFG
        {
            Settings = new ZombieModuleSettings
            {
                Category = "Zombie Items",
                GodModeDuration = 20f,
                InfiniteAmmoDuration = 20f
            },
            Items =
            [
                new ZombieItemTemplate
                {
                    Id = "t_virus_serum",
                    DisplayName = "T-Virus Serum",
                    DisplayNameKey = "item.t_virus_serum.name",
                    Price = 500,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.T),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "t_virus_reagent",
                    DisplayName = "T-Virus Reagent",
                    DisplayNameKey = "item.t_virus_reagent.name",
                    Price = 800,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "infection_grenade",
                    DisplayName = "Infection Grenade",
                    DisplayNameKey = "item.infection_grenade.name",
                    Price = 800,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.T),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "scba_suit",
                    DisplayName = "SCBA Suit",
                    DisplayNameKey = "item.scba_suit.name",
                    Price = 600,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "god_mode",
                    DisplayName = "God Mode",
                    DisplayNameKey = "item.god_mode.name",
                    Price = 1000,
                    SellPrice = null,
                    DurationSeconds = 20,
                    Type = nameof(ShopItemType.Temporary),
                    Team = nameof(ShopItemTeam.Any),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "add_health",
                    DisplayName = "Add Health",
                    DisplayNameKey = "item.add_health.name",
                    Price = 300,
                    SellPrice = null,
                    HealthAmount = 200,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "infinite_ammo",
                    DisplayName = "Infinite Ammo",
                    DisplayNameKey = "item.infinite_ammo.name",
                    Price = 900,
                    SellPrice = null,
                    DurationSeconds = 20,
                    Type = nameof(ShopItemType.Temporary),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "fire_grenade",
                    DisplayName = "Fire Grenade",
                    DisplayNameKey = "item.fire_grenade.name",
                    Price = 800,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "light_grenade",
                    DisplayName = "Light Grenade",
                    DisplayNameKey = "item.light_grenade.name",
                    Price = 500,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "freeze_grenade",
                    DisplayName = "Freeze Grenade",
                    DisplayNameKey = "item.freeze_grenade.name",
                    Price = 700,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "teleport_grenade",
                    DisplayName = "Teleport Grenade",
                    DisplayNameKey = "item.teleport_grenade.name",
                    Price = 1200,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                },
                new ZombieItemTemplate
                {
                    Id = "incendiary_grenade",
                    DisplayName = "Incendiary Grenade",
                    DisplayNameKey = "item.incendiary_grenade.name",
                    Price = 800,
                    SellPrice = null,
                    Type = nameof(ShopItemType.Consumable),
                    Team = nameof(ShopItemTeam.CT),
                    Enabled = true,
                    CanBeSold = false
                }
            ]
        };
    }
}

internal sealed class ZombieItemData
{
    public string ItemId { get; set; } = string.Empty;
    public int HealthAmount { get; set; }
}