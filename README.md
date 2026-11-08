# SwarmUI SnapRef

A [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) extension for the [SnapRef](https://github.com/ArcAnima/SnapRef) reference adapter for anima. It works with any standard anima model, including turbo.

## Install

Just clone it into your extensions folder. The node is included, and the selected model will be downloaded on the first generation.

Remote comfyui installs will require manually moving the included node into that installation's custom node folder.

## Use

1. Drop your reference picture into the prompt box.
2. Enable the **SnapRef** group.
3. Prompt the model like normal. The snapref repo has some guidelines, but regular prompts should be enough. You don't need to specify character details unless you're trying to change them. Results and flexibility tend to vary with the input image.

Up to three prompt images are used, any more are ignored.

There are some extra parameters you can play with which are fairly self explanatory, results vary.

For two characters, stitch them side by side into one picture and prompt `2girls`.

## Network connections

The only connection this extension makes is the weights download from Hugging Face
(`https://huggingface.co/0ppxnhximxr/snapref-<version>/resolve/main/snapref_<version>.safetensors`). It happens once
per version, during the first generation with SnapRef enabled that needs that version. To avoid it, put the
`.safetensors` file in your LoRA folder yourself before generating; nothing is downloaded when it is already there.
The bundled ComfyUI node makes no connections.

## Credits

SnapRef, its weights and its ComfyUI node are by [ArcAnima](https://github.com/ArcAnima/SnapRef). This extension only
connects them to SwarmUI.

## License

- This extension (`SnapRefExtension.cs`, `SnapRef.csproj`, this README): Apache-2.0, see `LICENSE`.
- `comfy_node/snapref/`: unmodified copy of the SnapRef ComfyUI node, Apache-2.0, see `comfy_node/snapref/LICENSE`.
- SnapRef weights: a derivative of the CircleStone Model under the CircleStone Labs Non-Commercial License v1.2.
  See the [model card](https://huggingface.co/0ppxnhximxr/snapref-v2a).
