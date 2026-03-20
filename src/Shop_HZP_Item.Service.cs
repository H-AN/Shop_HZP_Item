using HanZombiePlagueS2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace Shop_HZP_Item;

public class ShopHZPItemService
{
    private readonly ILogger<ShopHZPItemService> _logger;
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<ShopHZPItemCFG> _cfg;
    public ShopHZPItemService(ISwiftlyCore core, ILogger<ShopHZPItemService> logger,
        IOptionsMonitor<ShopHZPItemCFG> CFG)
    {
        _core = core;
        _logger = logger;
        _cfg = CFG;
    }



}
