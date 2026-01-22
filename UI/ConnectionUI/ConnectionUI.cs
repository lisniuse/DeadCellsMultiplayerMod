using System.Collections.Generic;
using dc;
using dc.en.inter;
using dc.h2d;
using dc.h3d.mat;
using dc.hl.types;
using dc.hxd;
using dc.hxd.res;
using dc.libs.heaps;
using dc.libs.heaps.slib;
using dc.pr;
using dc.shader;
using dc.tool;
using dc.ui;
using dc.ui.sel;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Utitities;
using Serilog;
using DeadCellsMultiplayerMod.Tools;
using dc.h3d.pass;
using dc.h3d.shader;
using dc.en;

namespace DeadCellsMultiplayerMod.MultiplayerModUI.Connection
{
    public class ConnectionUI :
    Process,
    IEventReceiver
    {
        private Flow? rootFlow;
        private UIBox? bg;
        private dc.h2d.Interactive? inter;
        private Flow? spritesflow;
        private Flow? MainTitleflow;
        private readonly List<HSprite> sprites = new();

        private static ConnectionUI? Instance;
        private HSprite? spriteui;

        public ConnectionUI(Process parent) : base(parent)
        {
            this.createRoot(parent.root);
            this.BuildUI();
            EventSystem.AddReceiver(this);
        }

        private void BuildUI()
        {
            this.clean();

            this.rootFlow = new Flow(null);
            this.rootFlow.set_isVertical(true);
            this.rootFlow.multiline = true;
            this.rootFlow.set_verticalAlign(new FlowAlign.Middle());
            this.rootFlow.set_horizontalAlign(new FlowAlign.Right());


            base.root.addChild(this.rootFlow);
            this.onResize();
            List<(string loColor, string hiColor)> colorPairs = new List<(string, string)>
            {
                ("#FF0000", "#0000FF"),
                ("#00FF00", "#FF00FF"),
                ("#FFFF00", "#FF0000"),
                ("#00FFFF", "#FF00FF"),
                ("#FFA500", "#800080"),
                ("#FF69B4", "#4169E1"),
            };


            for (int i = 0; i < sprx.Count; i++)
            {
                loadspr(sprx[i], null, null);
            }
        }


        private List<double> sprx = new List<double> { 0, -0.9, -0.3, -0.6 };
        private List<string> animlist = new List<string>
        {
            "idle", "run", "jumpUp", "jumpDown",
            "rolling",  "blockHoldShield", "runB",
            "yes","wineSpit","winePose","wineDrink",
        };
        private void loadspr(double x, string? loColorHex, string? hiColorHex)
        {
            this.spritesflow = new Flow(null);
            this.spritesflow.set_verticalAlign(new FlowAlign.Top());
            this.spritesflow.set_horizontalAlign(new FlowAlign.Middle());
            this.spritesflow.isVertical = false;

            int ptr = 0;

            dc.String idle ="idle".AsHaxeString();
            SpriteLib g = Assets.Class.getHeroLib(Cdb.Class.getSkinInfo("PrisonerDefault".AsHaxeString()));

            this.spriteui = new HSprite(g, "idle".AsHaxeString(), new Ref<int>(ref ptr), null);
            //playallanims(this.spriteui);

            int loColor =0;
            int hiColor =0;

            if (loColorHex!=null &&hiColorHex!=null)
            {
                loColor = MultiColor.ColorFromHex(loColorHex);
                hiColor = MultiColor.ColorFromHex(hiColorHex);
            }

            // dc.shader.Base2d base2D1 = (dc.shader.Base2d)this.spriteui.getShader(dc.shader.Base2d.Class);
            // if (base2D1 !=null)
            // {
            //     base2D1.killAlpha__ = true;
            //     base2D1.pixelAlign__ = false;
            //     base2D1.hasUVPos__ = true;
            //     base2D1.isRelative__ = true;
            //     this.spriteui.addShader(base2D1);
            // }
            // dc.h3d.mat.Texture innerTex5 = this.spriteui.rawTile.innerTex;
            // int color = MultiColor.ColorFromHex("#4169E1");
            // dc.shader.Outline outline =new dc.shader.Outline(innerTex5,new Ref<int>(ref color));
            // this.spriteui.addShader(outline);


            // dc.h3d.mat.Texture normalMapFromGroup = this.spriteui.lib.getNormalMapFromGroup(idle);
            // dc.h3d.shader.NormalMap normal = new dc.h3d.shader.NormalMap(normalMapFromGroup);
            // this.spriteui.addShader(normal);

            // dc.shader.DirLighted dirLighted =new dc.shader.DirLighted();
            // this.spriteui.addShader(dirLighted);

            // initColorMap();
            GradientHiLo gradientHiLo = (GradientHiLo)this.spriteui.addShader(new GradientHiLo(loColor, hiColor, null));


            SpritePivot pivot = this.spriteui.pivot;
            pivot.centerFactorX = x;
            pivot.centerFactorY = 0.5;
            pivot.usingFactor = true;
            pivot.isUndefined = false;

            string skinanim = GetRandomAnimation(animlist);


            AnimManager animManager = this.spriteui.get_anim().play(skinanim.AsHaxeString(), null, null).loop(null);
            animManager.genSpeed = 0.5;

            this.spriteui.set_visible(true);
            sprites.Add(this.spriteui);

            this.spritesflow.addChild(this.spriteui);
            this.bg?.addChild(spritesflow);

        }

        private string GetRandomAnimation(List<string> values)
        {
            Random fallbackRandom = new Random();
            int fallbackIndex = fallbackRandom.Next(values.Count);
            return values[fallbackIndex];
        }
        private dc.h3d.mat.Texture loadColorMapTexture(string skinId)
        {
            try
            {
                Loader loader = Res.Class.get_loader();
                _Image @class = Image.Class;
                string path = $"atlas/{skinId}.png";
                dc.h3d.mat.Texture normalMapTexture = ImageExtender.Class.toNormalMap(
                    (Image)loader.loadCache(path.AsHaxeString(), @class)
                );
                return normalMapTexture;


            }
            catch (Exception e)
            {
                Log.Debug($"找不到颜色贴图: {skinId},{e}");
                return null!;
            }
        }


        public void playallanims(HSprite hSprite)
        {
            dynamic groups = hSprite.lib.groups;
            if (groups != null)
            {
                dynamic keysIterator = groups.keys();
                animlist.Clear();

                while (keysIterator.hasNext())
                {
                    string key = keysIterator.next().ToString();
                    if (!key.StartsWith("Atk", StringComparison.OrdinalIgnoreCase))
                    {
                        animlist.Add(key);
                    }
                }
            }
        }



        public void initColorMap()
        {
            dc.shader.ColorMap shader = (dc.shader.ColorMap)this.spriteui!.getShader(dc.shader.ColorMap.Class);
            if (shader != null)
            {
                this.spriteui.removeShader(shader);
            }

            dc.h3d.mat.Texture texture = Res.Class.load("atlas/beheaded_aladdin_s.png".AsHaxeString()).toTexture();
            dc.h3d.mat.Filter filter = new dc.h3d.mat.Filter.Nearest();
            filter = texture.set_filter(filter);

            virtual_colorMap_consoleCmdId_glowData_group_head_incompatibleHeads_item_model_onlyDefaultHead_scarfBlendMode_scarfs_ skinInfo = Cdb.Class.getSkinInfo("PrisonerDefault".AsHaxeString());
            dc.h3d.mat.Texture heroColorMap = Assets.Class.getHeroColorMap(skinInfo);
            dc.shader.ColorMap colorMap = (ColorMap)this.spriteui.addShader(new dc.shader.ColorMap(texture));

        }


        private void clean()
        {
            this.bg?.remove();
            this.rootFlow?.remove();
            this.inter?.remove();
            this.sprites.Clear();
        }


        public override void onResize()
        {
            base.onResize();

            if (this.rootFlow == null || base.root == null)
                return;

            var win = dc.hxd.Window.Class.getInstance();
            double screenWidth = win.get_width();
            double screenHeight = win.get_height();


            base.root.x = 0;
            base.root.y = 0;

            this.rootFlow.set_minWidth((int)(screenWidth * 0.5)); //宽度 30%
            this.rootFlow.set_minHeight((int)(screenHeight * 0.5)); // 高度 80%
            this.rootFlow.reflow();


            double flowW = this.rootFlow.get_outerWidth();
            double flowH = this.rootFlow.get_outerHeight();


            this.bg?.remove();
            this.bg = UIBox.Class.drawBoxValidation(
                (int)flowW,
                (int)flowH,
                Ref<int>.Null,
                Ref<int>.Null,
                null,
                true
            );
            base.root.addChild(this.bg);

            

            double posX = screenWidth - flowW - base.get_pixelScale.Invoke() * 20.0; // 离右边 20 像素
            double posY = (screenHeight - flowH) / 2.0;
            this.rootFlow.x = posX;
            this.rootFlow.y = posY;


            this.bg.x = posX;
            this.bg.y = posY;


            this.inter?.remove();
            this.inter = new dc.h2d.Interactive(screenWidth, screenHeight, this.bg, null);
            this.inter.onClick = new HlAction<Event>(this.OnClick);

            BGtext();
        }


        private void BGtext()
        {
            this.MainTitleflow =new Flow(null);
            
            dc.ui.Text label = Assets.Class.makeText(
               "DeadCellsMultiplayerMod".AsHaxeString(),
               0xFFFFFFF,
               true,
               null
           );
            label.scaleX =1;
            label.scaleY =1;
            dc.h2d.Text text = label;

            this.MainTitleflow.addChild(text);
            this.MainTitleflow.set_verticalAlign(new FlowAlign.Top());
            this.MainTitleflow.set_horizontalAlign(new FlowAlign.Right());

            this.bg!.addChild(this.MainTitleflow);
            this.MainTitleflow.x+=15;
        }


        private void OnClick(Event e)
        {

        }


        public static void Initialize(ModEntry entry)
        {
            entry.Logger.Information("\x1b[36m[[ConnectionUI] Initializing...]\x1b[0m");
            Hook__TitleScreen.__constructor__ += Hook_TitleScreen_Constructor;
            Hook_TitleScreen.onResize += Hook_TitleScreen_OnResize;
            Hook_TitleScreen.playMenu += Hook_TitleScreen_PlayMenu;
        }

        private static void Hook_TitleScreen_PlayMenu(Hook_TitleScreen.orig_playMenu orig, TitleScreen self)
        {
            orig(self);
            if (Instance == null)
            {
                Instance = new ConnectionUI(self);
                self.addChild(Instance);
            }
        }

        private static void Hook_TitleScreen_OnResize(Hook_TitleScreen.orig_onResize orig, TitleScreen self)
        {
            orig(self);
            Instance?.onResize();
        }

        private static void Hook_TitleScreen_Constructor(Hook__TitleScreen.orig___constructor__ orig, TitleScreen self, bool? titleLib)
        {
            orig(self, titleLib);
        }


    }
}
