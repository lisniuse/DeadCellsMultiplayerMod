using ModCore.Storage;
using ModCore.Utilities;
using Serilog;
using HaxeFile = dc.tool.File;
using HaxeFileHook = dc.tool.Hook__File;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace DeadCellsMultiplayerMod;

internal static class SavePathRedirect
{
    private const string LocalGseSaveFolderName = "GSE Saves";

    private static bool s_installed;
    private static ILogger? s_log;
    private static string? s_localSaveRoot;
    private static bool s_isApplyingPath;

    internal static void Install(ILogger? log)
    {
        s_log = log;
        EnsureLocalSaveRoot();
        ForceLocalSavePath("install");

        if (s_installed)
            return;

        HaxeFileHook.exists += Hook_File_exists;
        HaxeFileHook.getBytes += Hook_File_getBytes;
        HaxeFileHook.saveBytes += Hook_File_saveBytes;
        HaxeFileHook.delete += Hook_File_delete;
        HaxeFileHook.listFiles += Hook_File_listFiles;
        HaxeFileHook.pathToBackupZip += Hook_File_pathToBackupZip;
        HaxeFileHook.purgeOldBackups += Hook_File_purgeOldBackups;
        HaxeFileHook.dailyBackup += Hook_File_dailyBackup;
        HaxeFileHook.makeBackupZip += Hook_File_makeBackupZip;
        HaxeFileHook.transferLocalToCloud += Hook_File_transferLocalToCloud;
        HaxeFileHook.transferCloudToLocal += Hook_File_transferCloudToLocal;

        s_installed = true;
        s_log?.Information("[NetMod] Save path redirect installed: {Path}", s_localSaveRoot);
    }

    internal static string LocalSaveRoot => EnsureLocalSaveRoot();

    private static string EnsureLocalSaveRoot()
    {
        if (!string.IsNullOrWhiteSpace(s_localSaveRoot))
            return s_localSaveRoot;

        var root = IOPath.GetFullPath(IOPath.Combine(FolderInfo.GameRoot.FullPath, LocalGseSaveFolderName));
        IODirectory.CreateDirectory(root);
        s_localSaveRoot = root;
        return root;
    }

    private static void ForceLocalSavePath(string reason)
    {
        if (s_isApplyingPath)
            return;

        s_isApplyingPath = true;
        try
        {
            var target = EnsureLocalSaveRoot();
            var current = string.Empty;
            try { current = HaxeFile.Class.PATH?.ToString() ?? string.Empty; } catch { }

            if (string.Equals(NormalizePath(current), NormalizePath(target), StringComparison.OrdinalIgnoreCase))
                return;

            HaxeFile.Class.PATH = target.AsHaxeString();
            s_log?.Information("[NetMod] Save path redirected ({Reason}): {OldPath} -> {NewPath}", reason, current, target);
        }
        catch (Exception ex)
        {
            s_log?.Warning(ex, "[NetMod] Failed to redirect save path ({Reason})", reason);
        }
        finally
        {
            s_isApplyingPath = false;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return IOPath.GetFullPath(path.Trim())
                .TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd('\\', '/');
        }
    }

    private static T WithLocalSavePath<T>(string reason, Func<T> action)
    {
        ForceLocalSavePath(reason);
        return action();
    }

    private static void WithLocalSavePath(string reason, Action action)
    {
        ForceLocalSavePath(reason);
        action();
    }

    private static bool Hook_File_exists(HaxeFileHook.orig_exists orig, dc.String file)
    {
        return WithLocalSavePath("exists", () => orig(file));
    }

    private static dc.haxe.io.Bytes Hook_File_getBytes(HaxeFileHook.orig_getBytes orig, dc.String file)
    {
        return WithLocalSavePath("getBytes", () => orig(file));
    }

    private static void Hook_File_saveBytes(HaxeFileHook.orig_saveBytes orig, dc.String file, dc.haxe.io.Bytes b)
    {
        WithLocalSavePath("saveBytes", () => orig(file, b));
    }

    private static void Hook_File_delete(HaxeFileHook.orig_delete orig, dc.String file)
    {
        WithLocalSavePath("delete", () => orig(file));
    }

    private static dc.hl.types.ArrayObj Hook_File_listFiles(HaxeFileHook.orig_listFiles orig)
    {
        return WithLocalSavePath("listFiles", () => orig());
    }

    private static dc.String Hook_File_pathToBackupZip(HaxeFileHook.orig_pathToBackupZip orig, dc.Date date, int id)
    {
        return WithLocalSavePath("pathToBackupZip", () => orig(date, id));
    }

    private static void Hook_File_purgeOldBackups(HaxeFileHook.orig_purgeOldBackups orig)
    {
        WithLocalSavePath("purgeOldBackups", () => orig());
    }

    private static void Hook_File_dailyBackup(HaxeFileHook.orig_dailyBackup orig)
    {
        WithLocalSavePath("dailyBackup", () => orig());
    }

    private static void Hook_File_makeBackupZip(HaxeFileHook.orig_makeBackupZip orig, bool useCloud, dc.String reason)
    {
        WithLocalSavePath("makeBackupZip", () => orig(useCloud, reason));
    }

    private static bool Hook_File_transferLocalToCloud(HaxeFileHook.orig_transferLocalToCloud orig)
    {
        return WithLocalSavePath("transferLocalToCloud", () => orig());
    }

    private static bool Hook_File_transferCloudToLocal(HaxeFileHook.orig_transferCloudToLocal orig)
    {
        return WithLocalSavePath("transferCloudToLocal", () => orig());
    }
}
