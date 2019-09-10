# ImageFinder

Library for fast pattern image search in other image

## Usage

`ImageFinder.SetSource(Image)` — set source image.

`ImageFinder.Find(Image, Single)` — find pattern in source by similary threshold.

`ImageFinder.MakeScreenshot()` — returns screenshot of main display.

`ImageFinder.Find` returns list of `ImageFinder.Match`, where `ImageFinder.Match` is:

```C#
struct Match {
    public Rectangle Zone;
    public Single Similarity;
}
```

<sub>\* Note that source size is limited to 2560x2560 and pattern size is limited to 256x256.</sub>