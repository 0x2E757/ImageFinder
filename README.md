# ImageFinder

Library for fast pattern image search in other image

## Usage

`ImageFinder.SetSource(Image)` — set source image.

`ImageFinder.Find(Image, Single[, Rectangle])` — find pattern in source by similary threshold and search zone if specified.

`ImageFinder.MakeScreenshot()` — returns screenshot of main display.

Results are stored in `ImageFinder.LastMatches`, which is `List` of:

```C#
struct Match {
    public Rectangle Zone;
    public Single Similarity;
}
```

<sub>\* Note that source size is limited to 2560x2560 and pattern size is limited to 256x256.</sub>