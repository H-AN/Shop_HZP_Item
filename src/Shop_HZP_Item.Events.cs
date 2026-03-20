using HanZombiePlagueS2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace Shop_HZP_Item;

public class ShopHZPItemEvents
{
    private readonly ILogger<ShopHZPItemEvents> _logger;
    private readonly ISwiftlyCore _core;
    public ShopHZPItemEvents(ISwiftlyCore core, ILogger<ShopHZPItemEvents> logger)
    {
        _core = core;
        _logger = logger;
    }


}
