using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System.IO;

namespace BetaDoggo.Extensions.SnapRef;

/// <summary>Adds the SnapRef reference adapter for Anima (https://github.com/ArcAnima/SnapRef), with the prompt box images as the reference.</summary>
public class SnapRefExtension : Extension
{
    public const string FeatureID = "snapref";

    /// <summary>A downloadable set of SnapRef weights.</summary>
    public record class SnapRefWeights(string Repo, string FileName, string Hash)
    {
        public string Url => $"https://huggingface.co/0ppxnhximxr/{Repo}/resolve/main/{FileName}";
    }

    /// <summary>All known SnapRef weights, keyed by the value of <see cref="ModelVersion"/>. Hashes are from the SHA256SUMS file of each repo.</summary>
    public static Dictionary<string, SnapRefWeights> Weights = new()
    {
        ["v2a"] = new("snapref-v2a", "snapref_v2a.safetensors", "8a22fc28d060447544eda283b36efe2dcdbad7422cbc0b8d42828d55a8a61ab4"),
        ["v1a"] = new("snapref-v1a", "snapref_v1a.safetensors", "0b8c6897845bb721c46977eae034a95492aa39cf22342b345f7e0315f4bd81fd"),
        ["v1b"] = new("snapref-v1b", "snapref_v1b.safetensors", "a64f273a51c2f1db3e484507d8d4e74e1c98f74e2f4810cd1a86293a89599fc3")
    };

    public static T2IRegisteredParam<string> ModelVersion, Mode, RefCaption;

    public static T2IRegisteredParam<double> Strength, Fidelity, RefMegapixels;

    public static T2IParamGroup SnapRefGroup;

    public override void OnPreInit()
    {
        // Self-started ComfyUI instances load the bundled node pack from here
        ComfyUISelfStartBackend.CustomNodePaths.Add(Path.GetFullPath(Path.Join(FilePath, "comfy_node")));
    }

    public override void OnInit()
    {
        ComfyUIBackendExtension.NodeToFeatureMap["SnapRef"] = FeatureID;
        SnapRefGroup = new("SnapRef", Toggles: true, Open: false, Description: "Reference adapter for Anima: uses the images in the prompt box as pictures of the character.\nWrite only the scene in the prompt.\nUp to 3 images are used, and they must all show the same character.");
        ModelVersion = T2IParamTypes.Register<string>(new("SnapRef Model", "[SnapRef]\nWhich SnapRef weights to use. Missing weights are downloaded automatically on first use.\n'v2a' is the newest release, with a much stronger 'Character' mode.\n'v1b' has twice the LoRA rank of 'v1a'.",
            "v2a", GetValues: _ => [.. Weights.Keys], Group: SnapRefGroup, FeatureFlag: FeatureID, OrderPriority: 1, ChangeWeight: 2
            ));
        Mode = T2IParamTypes.Register<string>(new("SnapRef Mode", "[SnapRef]\n'Character & Style' takes the character and the art style from the picture.\n'Character' takes only the character, the style comes from the model and your prompt.",
            "cs", GetValues: _ => ["cs///Character & Style", "char///Character"], Group: SnapRefGroup, FeatureFlag: FeatureID, OrderPriority: 2
            ));
        Strength = T2IParamTypes.Register<double>(new("SnapRef Strength", "[SnapRef]\nHow much the picture counts.\n1 is as trained, 0 is off, 2 is twice.",
            "1", Min: 0, Max: 2, Step: 0.05, ViewType: ParamViewType.SLIDER, Group: SnapRefGroup, FeatureFlag: FeatureID, OrderPriority: 3, Examples: ["0.5", "1", "1.5"]
            ));
        Fidelity = T2IParamTypes.Register<double>(new("SnapRef Fidelity", "[SnapRef]\n1 keeps every detail of the picture, lower values follow the prompt more freely.\nValues below 1 are about 12% slower.",
            "1", Min: 0, Max: 1, Step: 0.05, ViewType: ParamViewType.SLIDER, Group: SnapRefGroup, FeatureFlag: FeatureID, OrderPriority: 4, Examples: ["0.5", "0.8", "1"]
            ));
        RefMegapixels = T2IParamTypes.Register<double>(new("SnapRef Megapixels", "[SnapRef]\nThe picture is shrunk (never enlarged) to this area, keeping its aspect ratio.\nSnapRef is trained at 1 or below.",
            "1", Min: 0.25, Max: 2, Step: 0.05, ViewType: ParamViewType.SLIDER, Group: SnapRefGroup, FeatureFlag: FeatureID, OrderPriority: 5, IsAdvanced: true
            ));
        RefCaption = T2IParamTypes.Register<string>(new("SnapRef Caption", "[SnapRef]\nOptional appearance tags of the picture (gender, solo, hair, eyes, outfit), with no name or artist.\nEmpty works.",
            "", IgnoreIf: "", Group: SnapRefGroup, FeatureFlag: FeatureID, OrderPriority: 6, IsAdvanced: true
            ));
        // -9.5 is after the model loader (LoRAs included), and before the steps that would otherwise act on the prompt images (-7)
        WorkflowGenerator.AddStep(g =>
        {
            if (!g.UserInput.TryGet(ModelVersion, out string version))
            {
                return;
            }
            if (!g.Features.Contains(FeatureID))
            {
                throw new SwarmUserErrorException("SnapRef parameters specified, but the SnapRef node isn't available on this backend");
            }
            if (!g.IsAnima())
            {
                throw new SwarmUserErrorException("SnapRef only works with Anima models");
            }
            if (!g.UserInput.TryGet(T2IParamTypes.PromptImages, out List<Image> references) || references.Count == 0)
            {
                throw new SwarmUserErrorException("SnapRef is enabled, but there is no image in the prompt box to use as the reference");
            }
            if (!Weights.TryGetValue(version, out SnapRefWeights weights))
            {
                throw new SwarmUserErrorException($"Unknown SnapRef model '{version}'");
            }
            // The original input still has to fill '${promptimages.0}' tags and the metadata, so only this generator's view of it loses the prompt images
            T2IParamInput withoutPromptImages = g.UserInput.Clone();
            withoutPromptImages.ExtraMeta = g.UserInput.ExtraMeta;
            withoutPromptImages.ParamsQueried = g.UserInput.ParamsQueried;
            withoutPromptImages.Remove(T2IParamTypes.PromptImages);
            g.UserInput = withoutPromptImages;
            double strength = g.UserInput.Get(Strength, 1);
            if (strength <= 0)
            {
                return;
            }
            string[] loraFolders = Program.ServerSettings.Paths.SDLoraFolder.Split(';');
            if (!loraFolders.Any(folder => File.Exists(Utilities.CombinePathWithAbsolute(Program.ServerSettings.Paths.ActualModelRoot, folder, weights.FileName))))
            {
                g.DownloadModel(weights.FileName, Utilities.CombinePathWithAbsolute(Program.ServerSettings.Paths.ActualModelRoot, loraFolders[0], weights.FileName), weights.Url, weights.Hash);
            }
            JObject inputs = new()
            {
                ["model"] = g.CurrentModel.Path,
                ["clip"] = g.CurrentTextEnc.Path,
                ["vae"] = g.CurrentVae.Path,
                ["adapter"] = weights.FileName,
                ["mode"] = g.UserInput.Get(Mode, "cs"),
                ["ref_in_uncond"] = false,
                ["ref_megapixels"] = g.UserInput.Get(RefMegapixels, 1),
                ["ref_caption"] = g.UserInput.Get(RefCaption, ""),
                ["fidelity"] = g.UserInput.Get(Fidelity, 1)
            };
            for (int i = 0; i < Math.Min(references.Count, 3); i++)
            {
                // Not resized to the generation size, the node does its own aspect-preserving shrink
                WGNodeData referenceImage = g.LoadImage(references[i], "${promptimages." + i + "}", false);
                inputs[i == 0 ? "image" : $"image_{i + 1}"] = referenceImage.Path;
                inputs[$"weight_{i + 1}"] = strength;
            }
            string snapRefNode = g.CreateNode("SnapRef", inputs);
            g.CurrentModel = g.CurrentModel.WithPath([snapRefNode, 0]);
        }, -9.5);
    }
}
