using dc;
using dc.h2d;
using dc.hxd;
using dc.pr;
using dc.ui;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using DeadCellsMultiplayerMod.Tools;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.UI;

internal sealed class BossTestTeleportUi :
    IEventReceiver,
    IOnHeroUpdate
{
    private const double ButtonWidth = 86.0;
    private const double ButtonHeight = 24.0;
    private const double ButtonX = 18.0;
    private const double ButtonY = 118.0;

    private readonly ModEntry _entry;
    private UIBox? _box;
    private dc.h2d.Text? _label;
    private dc.h2d.Interactive? _interactive;

    public BossTestTeleportUi(ModEntry entry)
    {
        _entry = entry;
        EventSystem.AddReceiver(this);
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        var net = GameMenu.NetRef;
        var shouldShow = net != null && net.IsAlive && net.IsHost && ModEntry.me != null;
        if (!shouldShow)
        {
            Clear();
            return;
        }

        Ensure();
    }

    private void Ensure()
    {
        var hud = HUD.Class.ME;
        var root = hud?.root;
        if (hud == null || root == null)
        {
            Clear();
            return;
        }

        if (_box == null || _box.parent == null || !ReferenceEquals(_box.parent, root))
            Build(root);

        var scale = hud.get_pixelScale.Invoke() * UiScale.GetResolutionScale();
        if (_box != null)
        {
            _box.x = ButtonX * scale;
            _box.y = ButtonY * scale;
            _box.visible = true;
        }
    }

    private void Build(dc.h2d.Object root)
    {
        Clear();

        _box = UIBox.Class.drawBoxValidation(
            (int)ButtonWidth,
            (int)ButtonHeight,
            Ref<int>.Null,
            Ref<int>.Null,
            null,
            false);
        root.addChild(_box);

        _label = Assets.Class.makeText("BOSS TP".AsHaxeString(), dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()), false, _box);
        _label.textColor = 0xFFFFFF;
        _label.scaleX = 0.45;
        _label.scaleY = 0.45;
        _label.x = 12;
        _label.y = 5;

        _interactive = new dc.h2d.Interactive(ButtonWidth, ButtonHeight, _box, null);
        _interactive.onClick = new HlAction<Event>(_ => OnClick());
    }

    private void OnClick()
    {
        try
        {
            _entry.TryHostBossTestTeleport();
        }
        catch
        {
        }
    }

    private void Clear()
    {
        try { _interactive?.remove(); } catch { }
        try { _label?.remove(); } catch { }
        try { _box?.remove(); } catch { }
        _interactive = null;
        _label = null;
        _box = null;
    }
}
