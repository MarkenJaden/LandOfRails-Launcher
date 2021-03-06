﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LandOfRailsLauncher.MinecraftLaunch.Core
{
    public class MLaunch
    {
        private static Regex argBracket = new Regex(@"\$\{(.*?)}");
        private const int DefaultServerPort = 25565;

        public const string SupportVersion = "1.15.2";
        public static readonly string[] DefaultJavaParameter = {
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+UseG1GC",
                "-XX:G1NewSizePercent=20",
                "-XX:G1ReservePercent=20",
                "-XX:MaxGCPauseMillis=50",
                "-XX:G1HeapRegionSize=16M"
            };

        public MLaunch(MLaunchOption option)
        {
            option.CheckValid();
            LaunchOption = option;
            Minecraft = option.StartProfile.Minecraft;
        }

        Minecraft Minecraft;
        public MLaunchOption LaunchOption { get; }

        /// <summary>
        /// Start Game
        /// </summary>
        public void Start()
        {
            GetProcess().Start();
        }

        /// <summary>
        /// Build game process and return it
        /// </summary>
        public Process GetProcess()
        {
            var native = new MNative(LaunchOption);
            native.CleanNatives();
            native.CreateNatives();

            string arg = string.Join(" ", CreateArg());
            Process mc = new Process();
            mc.StartInfo.FileName = LaunchOption.JavaPath;
            mc.StartInfo.Arguments = arg;
            mc.StartInfo.WorkingDirectory = Minecraft.path;
            mc.StartInfo.UseShellExecute = false;
            mc.StartInfo.RedirectStandardOutput = true;
            mc.StartInfo.RedirectStandardError = true;

            return mc;
        }

        public string[] CreateArg()
        {
            var profile = LaunchOption.StartProfile;

            var args = new List<string>();

            // Common JVM Arguments
            if (LaunchOption.JVMArguments != null)
                args.AddRange(LaunchOption.JVMArguments);
            else
                args.AddRange(DefaultJavaParameter);

            args.Add("-Xmx" + LaunchOption.MaximumRamMb + "m");

            if (!string.IsNullOrEmpty(LaunchOption.DockName))
                args.Add("-Xdock:name=" + handleEmpty(LaunchOption.DockName));
            if (!string.IsNullOrEmpty(LaunchOption.DockIcon))
                args.Add("-Xdock:icon=" + handleEmpty(LaunchOption.DockIcon));

            // Version-specific JVM Arguments
            var libArgs = new List<string>(profile.Libraries.Length);
            libArgs.AddRange(from item in profile.Libraries where !item.IsNative select handleEmpty(Path.GetFullPath(item.Path)));

            libArgs.Add(handleEmpty(Path.Combine(Minecraft.Versions, profile.Jar, profile.Jar + ".jar")));

            var libs = string.Join(Path.PathSeparator.ToString(), libArgs);

            var jvmdict = new Dictionary<string, string>
            {
                { "natives_directory", handleEmpty(profile.NativePath) },
                { "launcher_name", useNotNull(LaunchOption.GameLauncherName, "minecraft-launcher") },
                { "launcher_version", useNotNull(LaunchOption.GameLauncherVersion, "2") },
                { "classpath", libs }
            };

            if (profile.JvmArguments != null)
                args.AddRange(argumentInsert(profile.JvmArguments, jvmdict));
            else
            {
                args.Add("-Djava.library.path=" + handleEmpty(LaunchOption.StartProfile.NativePath));
                args.Add("-cp " + libs);
            }

            args.Add(profile.MainClass);

            // Game Arguments
            var gameDict = new Dictionary<string, string>
            {
                { "auth_player_name", LaunchOption.Session.Username },
                { "version_name", LaunchOption.StartProfile.Id },
                { "game_directory", handleEmpty(Minecraft.path) },
                { "assets_root", handleEmpty(Minecraft.Assets) },
                { "assets_index_name", profile.AssetId },
                { "auth_uuid", LaunchOption.Session.UUID },
                { "auth_access_token", LaunchOption.Session.AccessToken },
                { "user_properties", "{}" },
                { "user_type", "Mojang" },
                { "game_assets", handleEmpty(Minecraft.AssetLegacy) },
                { "auth_session", LaunchOption.Session.AccessToken },
                { "version_type", useNotNull(LaunchOption.VersionType, profile.TypeStr) }
            };

            args.AddRange(profile.GameArguments != null
                ? argumentInsert(profile.GameArguments, gameDict)
                : argumentInsert(profile.MinecraftArguments.Split(' '), gameDict));

            // Options
            if (!string.IsNullOrEmpty(LaunchOption.ServerIp))
            {
                args.Add("--server " + LaunchOption.ServerIp);

                if (LaunchOption.ServerPort != DefaultServerPort)
                    args.Add("--port " + LaunchOption.ServerPort);
            }

            if (LaunchOption.ScreenWidth <= 0 || LaunchOption.ScreenHeight <= 0) return args.ToArray();
            args.Add("--width " + LaunchOption.ScreenWidth);
            args.Add("--height " + LaunchOption.ScreenHeight);

            return args.ToArray();
        }

        string[] argumentInsert(string[] arg, Dictionary<string, string> dicts)
        {
            var args = new List<string>(arg.Length);
            foreach (string item in arg)
            {
                var m = argBracket.Match(item);

                if (m.Success)
                {
                    var argKey = m.Groups[1].Value; // ${argKey}
                    var argValue = "";

                    args.Add(dicts.TryGetValue(argKey, out argValue)
                        ? replaceByPos(item, argValue, m.Index, m.Length)
                        : item);
                }
                else
                    args.Add(handleEArg(item));
            }

            return args.ToArray();
        }

        string replaceByPos(string input, string replace, int startIndex, int length)
        {
            var sb = new StringBuilder(input);
            sb.Remove(startIndex, length);
            sb.Insert(startIndex, replace);
            return sb.ToString();
        }

        string useNotNull(string input1, string input2)
        {
            return handleEmpty(string.IsNullOrEmpty(input1) ? input2 : input1);
        }

        // handle empty string in --key=value style argument
        // --key=va lue => --key="va lue"
        string handleEArg(string input)
        {
            if (!input.Contains(" ") || !input.Contains("=")) return input;
            var s = input.Split('=');
            return s[0] + "=\"" + s[1] + "\"";

        }

        string handleEmpty(string input)
        {
            if (input.Contains(" "))
                return "\"" + input + "\"";
            return input;
        }
    }
}
