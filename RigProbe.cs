using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Endnight.Animation;
using RedLoader;
using RedLoader.Utils;
using Sons.Ai.Vail;
using SonsSdk;
using SonsSdk.Attributes;
using TheForest.Utils;
using UnityEngine;

namespace RigProbe;

public class RigProbe : SonsMod
{
    private static readonly Dictionary<string, string> PlayerParents = new();
    private static readonly Dictionary<string, string> PlayerPaths = new();

    protected override void OnSdkInitialized()
    {
        RLog.Msg("RigProbe loaded. Commands: rigprobe [filter], rigspawn <TypeName> [variation]");
    }

    [DebugCommand("rigprobe")]
    private static void ProbeCommand(string args)
    {
        try
        {
            Probe((args ?? string.Empty).Trim());
        }
        catch (Exception e)
        {
            RLog.Error($"RigProbe failed: {e}");
        }
    }

    [DebugCommand("rigspawn")]
    private static void SpawnCommand(string args)
    {
        var parts = (args ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Enum.TryParse<VailActorTypeId>(parts[0], true, out var id))
        {
            Say("Usage: rigspawn <TypeName> [variation]  e.g. rigspawn Brandy");
            return;
        }

        var variation = parts.Length > 1 && int.TryParse(parts[1], out var v) ? v : 0;
        var player = LocalPlayer.GameObject;
        if (!player)
        {
            Say("Load into a game first");
            return;
        }

        try
        {
            var t = player.transform;
            var actor = ActorTools.Spawn(id, t.position + t.forward * 4f, variation);
            Say(actor ? $"Spawned {id} variation {variation}" : $"Spawn returned null for {id}");
        }
        catch (Exception e)
        {
            RLog.Error($"rigspawn {id} failed: {e.Message}");
        }
    }

    private static void Probe(string filter)
    {
        var player = LocalPlayer.GameObject;
        var race = LocalPlayer.RaceSystem;
        if (!player || !race)
        {
            Say("Load into a game first");
            return;
        }

        var root = race._animationRoot;
        if (!root)
            root = player.transform.Find("PlayerAnimator/Root");
        if (!root)
        {
            Say("Could not find the player animation root");
            return;
        }

        BuildPlayerRig(root);

        var sb = new StringBuilder();
        sb.AppendLine($"RigProbe {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Player animation root: {HierPath(root)}");
        sb.AppendLine($"Player rig transforms: {PlayerPaths.Count}");
        sb.AppendLine($"Current race: {race.CurrentRace}");
        try
        {
            sb.AppendLine($"ExpressionBlendsStartIndex: {race.GetExpressionBlendsStartIndex()}");
        }
        catch (Exception e)
        {
            sb.AppendLine($"ExpressionBlendsStartIndex: error {e.Message}");
        }
        sb.AppendLine($"Head: {Name(race.GetHead())}  LeftArm: {Name(race.GetLeftArm())}  RightArm: {Name(race.GetRightArm())}");
        sb.AppendLine();

        sb.AppendLine("==== PLAYER RENDERERS ====");
        DumpObject(sb, "Player", player, true);
        sb.AppendLine();

        sb.AppendLine("==== ACTORS ====");
        var ids = Enum.GetValues(typeof(VailActorTypeId)).Cast<VailActorTypeId>()
            .Where(i => i != VailActorTypeId.None)
            .Where(i => filter.Length == 0 || i.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        int probed = 0;
        foreach (var id in ids)
        {
            VailActor prefab = null;
            try
            {
                prefab = ActorTools.GetPrefab(id);
            }
            catch (Exception e)
            {
                sb.AppendLine($"[{id} prefab] error {e.Message}");
            }
            if (prefab)
            {
                DumpObject(sb, $"{id} prefab", prefab.gameObject, false);
                probed++;
            }

            VailActor live = null;
            try
            {
                live = ActorTools.GetActors(id)?.FirstOrDefault(a => a);
            }
            catch (Exception e)
            {
                sb.AppendLine($"[{id} live] error {e.Message}");
            }
            if (live)
            {
                DumpObject(sb, $"{id} live", live.gameObject, false);
                probed++;
            }
        }

        var dir = LoaderEnvironment.UserDataDirectory;
        var outPath = Path.Combine(dir, "RigProbe.txt");
        File.WriteAllText(outPath, sb.ToString());

        var rig = new StringBuilder();
        foreach (var kv in PlayerPaths.OrderBy(k => k.Value))
            rig.AppendLine(kv.Value);
        File.WriteAllText(Path.Combine(dir, "RigProbe_PlayerRig.txt"), rig.ToString());

        Say($"RigProbe: {probed} actor objects written to {outPath}");
    }

    private static void BuildPlayerRig(Transform root)
    {
        PlayerParents.Clear();
        PlayerPaths.Clear();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (PlayerPaths.ContainsKey(t.name))
                continue;
            PlayerPaths[t.name] = RelPath(t, root);
            PlayerParents[t.name] = t.parent ? t.parent.name : string.Empty;
        }
    }

    private static void DumpObject(StringBuilder sb, string label, GameObject go, bool allBlendShapes)
    {
        var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var swaps = go.GetComponentsInChildren<Sons.Ai.SwapSkinnedMeshWithPrefab>(true);

        int totalBones = 0, totalName = 0, totalParent = 0, remapCount = 0;
        var body = new StringBuilder();

        foreach (var r in renderers)
        {
            var a = Analyze(r, allBlendShapes);
            totalBones += a.Bones;
            totalName += a.NameMatch;
            totalParent += a.ParentMatch;
            if (a.HasRemap)
                remapCount++;
            body.Append(a.Text);
        }

        var summary = $"[{label}] go={go.name} renderers={renderers.Length} bones={totalBones} " +
                      $"nameMatch={Pct(totalName, totalBones)} parentMatch={Pct(totalParent, totalBones)} " +
                      $"remapCaches={remapCount} meshSwappers={swaps.Length}";
        sb.AppendLine(summary);
        foreach (var s in swaps)
        {
            string asset;
            try
            {
                asset = s._meshAsset ? s._meshAsset.name : "null";
            }
            catch
            {
                asset = "?";
            }
            sb.AppendLine($"  swapper {HierPath(s.transform)} meshAsset={asset}");
        }
        sb.Append(body);
        sb.AppendLine();
        RLog.Msg(summary);
    }

    private sealed class Result
    {
        public int Bones;
        public int NameMatch;
        public int ParentMatch;
        public bool HasRemap;
        public string Text;
    }

    private static Result Analyze(SkinnedMeshRenderer r, bool allBlendShapes)
    {
        var res = new Result();
        var missing = new List<string>();
        var parentDiff = new List<string>();
        int nulls = 0;

        var bones = r.bones;
        if (bones != null)
        {
            foreach (var b in bones)
            {
                if (!b)
                {
                    nulls++;
                    continue;
                }
                res.Bones++;
                if (PlayerParents.TryGetValue(b.name, out var expectedParent))
                {
                    res.NameMatch++;
                    var actualParent = b.parent ? b.parent.name : string.Empty;
                    if (actualParent == expectedParent)
                        res.ParentMatch++;
                    else
                        parentDiff.Add($"{b.name}({actualParent}!={expectedParent})");
                }
                else
                {
                    missing.Add(b.name);
                }
            }
        }

        var sb = new StringBuilder();
        var mesh = r.sharedMesh;
        int blendCount = mesh ? mesh.blendShapeCount : 0;

        sb.AppendLine($"  SMR {HierPath(r.transform)} active={r.gameObject.activeInHierarchy} mesh={(mesh ? mesh.name : "null")} " +
                      $"verts={(mesh ? mesh.vertexCount : 0)} bones={res.Bones} null={nulls} nameMatch={res.NameMatch} " +
                      $"parentMatch={res.ParentMatch} rootBone={(r.rootBone ? r.rootBone.name : "null")} blendShapes={blendCount} " +
                      $"materials={string.Join(",", r.sharedMaterials.Where(m => m).Select(m => m.name))}");

        var remap = r.GetComponent<SkinnedMeshBoneRemapCache>();
        if (remap)
        {
            res.HasRemap = true;
            sb.AppendLine($"    remap compat={remap._isCompatible} rootPath={remap._rootBonePath} " +
                          $"paths={(remap._bonePaths != null ? remap._bonePaths.Count : 0)} " +
                          $"srcBase={remap._sourceTransformBasePath} base={remap._transformBasePath}");
        }

        if (missing.Count > 0)
            sb.AppendLine($"    missing({missing.Count}): {string.Join(" ", missing.Take(60))}");
        if (parentDiff.Count > 0)
            sb.AppendLine($"    parentDiff({parentDiff.Count}): {string.Join(" ", parentDiff.Take(30))}");

        if (mesh && blendCount > 0)
        {
            var take = allBlendShapes ? blendCount : Math.Min(blendCount, 15);
            var names = new List<string>();
            for (int i = 0; i < take; i++)
                names.Add($"{i}:{mesh.GetBlendShapeName(i)}");
            sb.AppendLine($"    blendShapes: {string.Join(" ", names)}{(take < blendCount ? " ..." : string.Empty)}");
        }

        res.Text = sb.ToString();
        return res;
    }

    private static string Pct(int a, int b)
    {
        return b == 0 ? "n/a" : $"{a}/{b} ({a * 100 / b}%)";
    }

    private static string Name(GameObject go)
    {
        return go ? go.name : "null";
    }

    private static string HierPath(Transform t)
    {
        var parts = new List<string>();
        while (t)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string RelPath(Transform t, Transform root)
    {
        var parts = new List<string>();
        while (t && t != root)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void Say(string text)
    {
        SonsTools.ShowMessage(text, 6f);
        RLog.Msg(text);
    }
}
