# Godot 4 compositor lens effects 🌤️
Lens flares and god rays implemented in a Godot 4 compositor effect.

Should work with Godot **4.4+**. If not, please open an issue :)

It is also available on the new Godot Asset Store for free:
https://store.godotengine.org/asset/arez/compositor-lens-flare-godrays/

And therefore it is also available inside the engine itself!
If you use the shader and enjoy it, I would also appreciate a like on the asset store :)

---

Compositor effects are really cool. They allow you to hook into the rendering pipeline of Godot and write some nice low level shader code, while optionally having access to the shader data that Godot itself uses.

Here are some screenshots of the editor viewport (because compositor effects just run in the editor viewport too!):
![screenshot1](media/screenshot1.png)
![screenshot2](media/screenshot2.png)
![screenshot3](media/screenshot3.png)
![screenshot3](media/screenshot4.png)
![screenshot3](media/screenshot5.png)
![screenshot3](media/screenshot6.png)

And here's a video (also just a recording of the viewport, sorry for the quality):

https://github.com/user-attachments/assets/9afded05-fc36-4b94-bdd1-1c783292b4f2




## Usage
1. Download the `lens_effects` folder into your projects `res://addons/` folder
2. Create or select a `WorldEnvironment` node
3. Under `Compositor`, create a new `Compositor` and add 1 slot to the effects array
4. Select the empty slot and create a new `LensFlareEffect` (it should show up for you even without activating the plugin)
5. Add the `world_environment.gd` script (also found in the `lens_effects` folder) to the WorldEnvironment node and assign the `sun` exported variable to point at your `DirectionalLight3D`.   **Note:** You don't have to have the script on the WorldEnvironment itself. I have added some notes inside the script what to modify, if you want to have the script running on some other node.

6. Reload the scene (via "Scene - Reload saved scene") to load the toolscript
7. Tweak some values in the `LensFlareEffect` and enjoy :)

I have tried to add some comments here and there and also hover descriptions for the parameters of the effect. Hopefully that helps.

Also check out the demo scene provided.

**Note:** If you make changes to the GLSL shader file, the easiest way to reload the changes that I found is to click "Scene - Reload Saved Scene".

### Tips
- I have added hover descriptions to most settings of the effect, so feel free to use those
- To adjust how visible the rays are, the `weight` setting is useful
- If the rays look too noisy for your liking, adjust the `sample` setting (you can try `50, 100, 200, ...`)
- For advanced users: I have added some `#define`s at the top of the shader. Those can yield a small performance boost, but are probably not that important. Stuff like the number of samples should be much more important.

### Double precision
If you build your engine with double precision (like I do for my project) then you should uncomment the line
```
#define USE_DOUBLE_PRECISION
```
inside of [addons/lens_effects/lens_flares.glsl](addons/lens_effects/lens_flares.glsl)

## In case of any errors/ weird behaviour
I am not sure why, but I have had strange problems before which I fixed without changing anything in the shader. I am guessing it's some issue with Godot but not sure.

Try these steps:
- select the `lens_flares.glsl` file
- go to the "Import" tab
- click "Reimport"
- now try "Scene" - "Reload Saved Scene"
- (alternatively, try "Project" - "Reload Current Project")

I hope that fixes some issues you might be having :) 

## Thanks to
[pink-arcana](https://github.com/pink-arcana) for their amazing example project for compositor effects here:
- https://github.com/pink-arcana/godot-distance-field-outlines
I have reused the `base_compositor_effect.gd` file with some slight modifications.

<br>

The following shadertoys:
- https://www.shadertoy.com/view/wlcyzj
- https://www.shadertoy.com/view/XsKGRW
