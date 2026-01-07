
using System.Security.Cryptography;
using dc;
using dc.en;
using dc.h2d;
using dc.pr;
using dc.tool.log;
using dc.ui;
using dc.ui.hud;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utitities;

namespace DeadCellsMultiplayerMod;

public class MultiplayerUI
{
    public ModEntry mod { get; set; }
    public dc.ui.hud.LifeBar kingLife { get; set; } = null!;
    public dc.h2d.Flow toplib { get; set; } = null!;
    public FlowBox box { get; set; } = null!;
    public MultiplayerUI(ModEntry Entry)
    {
        mod = Entry;

    }

    public void init()
    {
        Hook_HUD.initLeftFlowT += Hook_HUD_initLeftFlowT;
        Hook_HUD.initHero += Hook_HUD_initking;

        Hook_Hero.updateLifeBar += Hook_Hero_kinglifupdate;
    }

    private void Hook_HUD_initking(Hook_HUD.orig_initHero orig, HUD self)
    {
        orig(self);
        initkingLife(self);

    }

    private void Hook_Hero_kinglifupdate(Hook_Hero.orig_updateLifeBar orig, Hero self)
    {
        orig(self);
        var king = ModEntry._companionKing;
        if (king == null) return;
        kinglifeupdate(king, king.life, king.maxLife, king.life, king.bonusLife, king.radius);
    }

    private void Hook_HUD_initLeftFlowT(Hook_HUD.orig_initLeftFlowT orig, HUD self)
    {
        orig(self);
        this.toplib = self.topRightFlowT;
        dc.ui.hud.LifeBar kingLifeBar = new dc.ui.hud.LifeBar(new LifeBarColorMode.Normal(), this.toplib);
        this.kingLife = kingLifeBar;
    }

    public void initkingLife(HUD self)
    {
        double wh = 40;
        double hh = 15;
        bool logo = true;
        FlowBox uibox = FlowBox.Class.createBoxValidation(null, Ref<double>.From(ref wh), Ref<double>.From(ref hh), Ref<bool>.From(ref logo), null);
        this.box = uibox;

        dc.String str = "gamer:".AsHaxeString();
        dc.String remoteUsername = GameMenu.RemoteUsername.AsHaxeString();
        dc.String combined = dc.String.Class.__add__(str, remoteUsername);
        dc.h2d.Text text_h2d = Assets.Class.makeText(combined, dc.ui.Text.Class.COLORS.get("ST".AsHaxeString()), false, this.box);
        self.topRightFlowT.addChild(this.box);

        this.toplib.set_verticalAlign(new FlowAlign.Top());
        this.toplib.set_horizontalAlign(new FlowAlign.Left());

        var geth = Viewport.Class.NATIVE_HEIGHT;
        var getw = Viewport.Class.NATIVE_WIDTH;

        double pixelScale = self.get_pixelScale.Invoke();

        int rightMargin = (int)(5 * pixelScale);
        int topMargin = (int)(5 * pixelScale);


        int w = (int)(400 * pixelScale);
        int h = (int)(15 * pixelScale);

        int targetX = getw - w - rightMargin;
        int targetY = topMargin;

        this.box.x = targetX;
        this.box.y = targetY;

        this.kingLife.setSize(w, h);
        this.kingLife.get_pixelScale = self.get_pixelScale;
        this.kingLife.enableText();
    }

    public void kinglifeupdate(KingSkin king, double max, double maxLife, double lif, double? bonusLife, double recover)
    {
        var k = this.kingLife;
        k.init(max, maxLife);
        k.curState.life = lif;
        k.curState.bonusLife = (double)bonusLife!;
        k.curState.recover = recover;
    }
}
