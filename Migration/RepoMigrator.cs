using System;
using System.Collections;
using System.Reflection;
using FCCH.Common;

namespace FCCH.Migration
{
    internal static class RepoMigrator
    {
        private const string InternalName = "FCCH";
        private const string NewRepoUrl = "https://puni.sh/api/repository/nexai";
        private const string MigrationReason = "Repo migration to puni.sh";

        private const string DalamudConfigurationType = "Dalamud.Configuration.Internal.DalamudConfiguration";
        private const string PluginManagerType = "Dalamud.Plugin.Internal.PluginManager";
        private const string ThirdPartyRepoSettingsType = "Dalamud.Configuration.ThirdPartyRepoSettings";
        private const string ServiceGenericType = "Dalamud.Service`1";

        private const string ManifestField = "manifest";
        private const string InstalledFromUrlMember = "InstalledFromUrl";
        private const string SaveManifestMethod = "SaveManifest";
        private const string QueueSaveMethod = "QueueSave";
        private const string ReloadReposMethod = "SetPluginReposFromConfigAsync";
        private const string ThirdRepoListMember = "ThirdRepoList";
        private const string InstalledPluginsMember = "InstalledPlugins";
        private const string InternalNameMember = "InternalName";
        private const string UrlMember = "Url";
        private const string IsEnabledMember = "IsEnabled";
        private const string LocalDevPluginTypeName = "LocalDevPlugin";

        private const int ReloadDelayTicks = 2;
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static void Run()
        {
            try
            {
                var localPlugin = FindInstalledPlugin();
                if (localPlugin == null) return;

                var manifest = GetMember(localPlugin, ManifestField);
                if (manifest == null) return;

                if (GetMember(manifest, InstalledFromUrlMember) as string == NewRepoUrl) return;

                AddRepoIfMissing();
                SetMember(manifest, InstalledFromUrlMember, NewRepoUrl);
                InvokeMethod(localPlugin, SaveManifestMethod, new object[] { MigrationReason });
                SaveConfiguration();
                ScheduleRepoReload();

                FCCHLog.Info($"Repo migration complete: {InternalName} now updates from {NewRepoUrl}");
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "Repo migration to puni.sh failed");
            }
        }

        private static object? FindInstalledPlugin()
        {
            var pluginManager = GetService(PluginManagerType);
            if (GetMember(pluginManager, InstalledPluginsMember) is not IEnumerable installed) return null;

            foreach (var plugin in installed)
            {
                if (plugin == null) continue;
                if (plugin.GetType().Name == LocalDevPluginTypeName) continue;
                if (GetMember(plugin, InternalNameMember) as string == InternalName) return plugin;
            }

            return null;
        }

        private static void AddRepoIfMissing()
        {
            var config = GetService(DalamudConfigurationType);
            if (GetMember(config, ThirdRepoListMember) is not IList repoList) return;

            foreach (var repo in repoList)
            {
                if (repo == null) continue;
                if (GetMember(repo, UrlMember) as string == NewRepoUrl) return;
            }

            var entry = Activator.CreateInstance(ResolveType(ThirdPartyRepoSettingsType));
            if (entry == null) return;

            SetMember(entry, UrlMember, NewRepoUrl);
            SetMember(entry, IsEnabledMember, true);
            repoList.Add(entry);
        }

        private static void SaveConfiguration()
        {
            var config = GetService(DalamudConfigurationType);
            InvokeMethod(config, QueueSaveMethod, Array.Empty<object>());
        }

        private static void ScheduleRepoReload()
        {
            _ = Plugin.Framework.RunOnTick(ReloadPluginMasters, delayTicks: ReloadDelayTicks);
        }

        private static void ReloadPluginMasters()
        {
            try
            {
                var pluginManager = GetService(PluginManagerType);
                InvokeMethod(pluginManager, ReloadReposMethod, new object[] { true });
            }
            catch (Exception ex)
            {
                FCCHLog.Error(ex, "Plugin repo reload after migration failed");
            }
        }

        private static object GetService(string serviceTypeFullName)
        {
            var serviceType = ResolveType(serviceTypeFullName);
            var serviceAccessor = ResolveType(ServiceGenericType).MakeGenericType(serviceType);
            var getMethod = serviceAccessor.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(serviceAccessor.FullName, "Get");

            return getMethod.Invoke(null, null)
                ?? throw new InvalidOperationException($"Dalamud service {serviceTypeFullName} resolved to null");
        }

        private static Type ResolveType(string fullName) => DalamudAssembly.GetType(fullName, true)!;

        private static Assembly DalamudAssembly => Plugin.PluginInterface.GetType().Assembly;

        private static object? GetMember(object target, string name)
        {
            var property = target.GetType().GetProperty(name, InstanceFlags);
            if (property != null) return property.GetValue(target);

            var field = FindField(target.GetType(), name);
            if (field != null) return field.GetValue(target);

            throw new MissingMemberException(target.GetType().FullName, name);
        }

        private static void SetMember(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name, InstanceFlags);
            if (property is { CanWrite: true })
            {
                property.SetValue(target, value);
                return;
            }

            var field = FindField(target.GetType(), name);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            throw new MissingMemberException(target.GetType().FullName, name);
        }

        private static object? InvokeMethod(object target, string name, object[] args)
        {
            var method = FindMethod(target.GetType(), name)
                ?? throw new MissingMethodException(target.GetType().FullName, name);

            return method.Invoke(target, args);
        }

        private static FieldInfo? FindField(Type? type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, InstanceFlags);
                if (field != null) return field;
            }

            return null;
        }

        private static MethodInfo? FindMethod(Type? type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var method = type.GetMethod(name, InstanceFlags);
                if (method != null) return method;
            }

            return null;
        }
    }
}
