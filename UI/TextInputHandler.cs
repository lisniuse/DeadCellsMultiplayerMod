using System.Reflection;
using dc.ui;
using Serilog;

namespace DeadCellsMultiplayerMod.UI
{
    internal static class TextInputHandler
    {
        private const int KeyCtrl = 17;
        private const int KeyLCtrl = 162;
        private const int KeyRCtrl = 163;
        private const int KeyC = 67;
        private const int KeyV = 86;
        private const int KeySpace = 32;
        private const int KeyEsc = 27;

        private static readonly object Sync = new();
        private static WeakReference<TextInput?>? _activeTextInputRef;
        private static bool _activeTextInputNoSpaces;

        public static void RegisterActiveTextInput(TextInput input, bool noSpaces)
        {
            lock (Sync)
            {
                _activeTextInputRef = new WeakReference<TextInput?>(input);
                _activeTextInputNoSpaces = noSpaces;
            }
        }

        public static void ClearActiveTextInput()
        {
            lock (Sync)
            {
                _activeTextInputRef = null;
                _activeTextInputNoSpaces = false;
            }
        }

        public static TextInput? GetActiveTextInput()
        {
            lock (Sync)
            {
                if (_activeTextInputRef != null && _activeTextInputRef.TryGetTarget(out var input))
                    return input;
            }
            return null;
        }

        public static void HandleClipboardShortcuts(ILogger? log)
        {
            var textInput = GetActiveTextInput();
            if (textInput == null) return;
            if (!IsTextInputActive(textInput))
            {
                ClearActiveTextInput();
                return;
            }
            if (GetActiveTextInputNoSpaces())
                RemoveSpacesFromTextInput(textInput);

            if (dc.hxd.Key.Class.isPressed(KeyEsc))
            {
                try
                {
                    textInput.cancel();
                }
                finally
                {
                    ClearActiveTextInput();
                }
                return;
            }

            if (dc.hxd.Key.Class.isPressed(KeySpace))
            {
                try
                {
                    textInput.validate();
                }
                finally
                {
                    ClearActiveTextInput();
                }
                return;
            }

            if (!IsCtrlDown())
                return;

            if (dc.hxd.Key.Class.isPressed(KeyC))
            {
                if (TryGetTextInputValue(textInput, out var text))
                    ClipboardHelper.TrySetClipboardText(text);
                return;
            }

            if (dc.hxd.Key.Class.isPressed(KeyV))
            {
                var clip = ClipboardHelper.TryGetClipboardText();
                if (!string.IsNullOrEmpty(clip))
                {
                    if (GetActiveTextInputNoSpaces())
                        clip = RemoveSpaces(clip);
                    TrySetTextInputValue(textInput, clip);
                }
            }
        }

        public static bool IsTextInputActive(TextInput input)
        {
            var active = TitleScreenReflection.GetMemberValue(input, "isActive", true) ?? TitleScreenReflection.GetMemberValue(input, "active", true);
            if (active is bool activeBool)
                return activeBool;

            var visible = TitleScreenReflection.GetMemberValue(input, "visible", true) ?? TitleScreenReflection.GetMemberValue(input, "isVisible", true);
            if (visible is bool visibleBool)
                return visibleBool;

            var target = GetTextInputTarget(input);
            var focused = TitleScreenReflection.GetMemberValue(target, "hasFocus", true) ?? TitleScreenReflection.GetMemberValue(target, "focused", true);
            if (focused is bool focusedBool)
                return focusedBool;

            return true;
        }

        public static bool TryGetTextInputValue(TextInput input, out string text)
        {
            text = string.Empty;
            var target = GetTextInputTarget(input);
            if (target == null)
                return false;

            var value = TitleScreenReflection.GetMemberValue(target, "text", true)
                ?? TitleScreenReflection.GetMemberValue(target, "value", true)
                ?? TitleScreenReflection.GetMemberValue(target, "str", true);
            if (value == null)
                return false;

            if (value is dc.String ds)
            {
                text = ds.ToString() ?? string.Empty;
                return true;
            }

            text = value.ToString() ?? string.Empty;
            return true;
        }

        public static bool TrySetTextInputValue(TextInput input, string text)
        {
            var target = GetTextInputTarget(input);
            if (target == null)
                return false;

            if (TryInvokeTextInputSetter(target, TitleScreenReflection.MakeHLString(text))
                || TryInvokeTextInputSetter(target, text))
                return true;

            return TitleScreenReflection.TrySetMember(target, "text", TitleScreenReflection.MakeHLString(text))
                || TitleScreenReflection.TrySetMember(target, "value", TitleScreenReflection.MakeHLString(text))
                || TitleScreenReflection.TrySetMember(target, "str", TitleScreenReflection.MakeHLString(text))
                || TitleScreenReflection.TrySetMember(target, "text", text)
                || TitleScreenReflection.TrySetMember(target, "value", text)
                || TitleScreenReflection.TrySetMember(target, "str", text);
        }

        public static void RemoveSpacesFromTextInput(TextInput input)
        {
            if (!TryGetTextInputValue(input, out var text))
                return;
            if (!text.Contains(' ', StringComparison.Ordinal))
                return;
            TrySetTextInputValue(input, RemoveSpaces(text));
        }

        public static string RemoveSpaces(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);

        private static object? GetTextInputTarget(TextInput input)
        {
            return TitleScreenReflection.GetMemberValue(input, "input", true)
                ?? TitleScreenReflection.GetMemberValue(input, "textInput", true)
                ?? TitleScreenReflection.GetMemberValue(input, "textField", true)
                ?? input;
        }

        private static bool TryInvokeTextInputSetter(object target, object value)
        {
            var type = target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (var name in new[] { "setText", "set_text", "setValue", "set_value" })
            {
                var method = type.GetMethod(name, flags);
                if (method == null) continue;
                try
                {
                    method.Invoke(target, new[] { value });
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static bool IsCtrlDown()
        {
            return dc.hxd.Key.Class.isDown(KeyCtrl) || dc.hxd.Key.Class.isDown(KeyLCtrl) || dc.hxd.Key.Class.isDown(KeyRCtrl);
        }

        internal static bool GetActiveTextInputNoSpaces()
        {
            lock (Sync) return _activeTextInputNoSpaces;
        }
    }
}
