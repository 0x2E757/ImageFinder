using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImageFinderNS {

    public static class ImageFinder {

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 3)]
        private struct Pixel {

            public Byte B;
            public Byte G;
            public Byte R;

        };

        private struct ImageContainer {

            public Bitmap Bitmap;
            public Graphics Graphics;

            public ImageContainer(Size size) {
                this.Bitmap = new Bitmap(size.Width, size.Height, PixelFormat);
                this.Graphics = Graphics.FromImage(this.Bitmap);
                this.Graphics.InterpolationMode = InterpolationMode;
                this.Graphics.PixelOffsetMode = PixelOffsetMode;
            }

        }

        private struct HashedImageData {

            public struct RGBWeight {

                public Int32 R;
                public Int32 G;
                public Int32 B;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void Reset() {
                    this.R = 0;
                    this.G = 0;
                    this.B = 0;
                }

            }

            public RGBWeight HSum;
            public RGBWeight VSum;
            public RGBWeight Diff;

        }

        private struct RGBDifference {

            public Single R;
            public Single G;
            public Single B;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void AddVSumDifference(HashedImageData* first, HashedImageData* second) {
                this.R += first->VSum.R > second->VSum.R ? (Single)second->VSum.R / (Single)first->VSum.R : (Single)first->VSum.R / (Single)second->VSum.R;
                this.G += first->VSum.G > second->VSum.G ? (Single)second->VSum.G / (Single)first->VSum.G : (Single)first->VSum.G / (Single)second->VSum.G;
                this.B += first->VSum.B > second->VSum.B ? (Single)second->VSum.B / (Single)first->VSum.B : (Single)first->VSum.B / (Single)second->VSum.B;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void AddHSumDifference(HashedImageData* first, HashedImageData* second) {
                this.R += first->HSum.R > second->HSum.R ? (Single)second->HSum.R / (Single)first->HSum.R : (Single)first->HSum.R / (Single)second->HSum.R;
                this.G += first->HSum.G > second->HSum.G ? (Single)second->HSum.G / (Single)first->HSum.G : (Single)first->HSum.G / (Single)second->HSum.G;
                this.B += first->HSum.B > second->HSum.B ? (Single)second->HSum.B / (Single)first->HSum.B : (Single)first->HSum.B / (Single)second->HSum.B;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void AddDiffDifference(HashedImageData* first, HashedImageData* second) {
                this.R += first->Diff.R > second->Diff.R ? (Single)second->Diff.R / (Single)first->Diff.R : (Single)first->Diff.R / (Single)second->Diff.R;
                this.G += first->Diff.G > second->Diff.G ? (Single)second->Diff.G / (Single)first->Diff.G : (Single)first->Diff.G / (Single)second->Diff.G;
                this.B += first->Diff.B > second->Diff.B ? (Single)second->Diff.B / (Single)first->Diff.B : (Single)first->Diff.B / (Single)second->Diff.B;
            }

        }

        public struct Match {

            public Rectangle Zone;
            public Single Similarity;

            public Match(Rectangle zone, Single similarity) {
                this.Zone = zone;
                this.Similarity = similarity;
            }

        }
        private enum FindState { None, Initial, Sequent }

        private static readonly PixelFormat PixelFormat;
        private static readonly InterpolationMode InterpolationMode;
        private static readonly PixelOffsetMode PixelOffsetMode;

        private static ImageContainer SourceImageContainer;
        private static ImageContainer TargetImageContainer;
        private static HashedImageData[,] SourceHashedImageData;
        private static HashedImageData[,] TargetHashedImageData;

        private static Image RawSourceImage;
        private static Image RawTargetImage;
        private static Single SimilarityThreshold;

        public static List<Match> LastMatches;

        static ImageFinder() {
            Size sourceImageSize = new Size(2560, 2560);
            Size targetImageSize = new Size(256, 256);
            PixelFormat = PixelFormat.Format24bppRgb;
            InterpolationMode = InterpolationMode.HighQualityBicubic;
            PixelOffsetMode = PixelOffsetMode.HighQuality;
            SourceImageContainer = new ImageContainer(sourceImageSize);
            TargetImageContainer = new ImageContainer(targetImageSize);
            SourceHashedImageData = new HashedImageData[sourceImageSize.Width, sourceImageSize.Height];
            TargetHashedImageData = new HashedImageData[targetImageSize.Width, targetImageSize.Height];
            LastMatches = new List<Match>();
        }

        public static Bitmap MakeScreenshot() {
            return MakeScreenshot(Screen.PrimaryScreen.Bounds);
        }

        public static Bitmap MakeScreenshot(Rectangle region) {
            if (region.Width > Screen.PrimaryScreen.Bounds.Size.Width)
                throw new Exception($"Specified region width can not be larger than primary screen width.");
            if (region.Height > Screen.PrimaryScreen.Bounds.Size.Height)
                throw new Exception($"Specified region height can not be larger than primary screen height.");
            Bitmap bitmap = new Bitmap(region.Width, region.Height);
            Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(region.Location, Point.Empty, region.Size);
            return bitmap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetSource(Image image) {
            if (image.Width > SourceImageContainer.Bitmap.Width)
                throw new Exception($"Specified image width can not be larger than ${SourceImageContainer.Bitmap.Width} pixels.");
            if (image.Height > SourceImageContainer.Bitmap.Height)
                throw new Exception($"Specified image height can not be larger than ${SourceImageContainer.Bitmap.Height} pixels.");
            RawSourceImage = image;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Find(Image image, Single similarityThreshold) {
            if (RawSourceImage == null)
                throw new Exception($"Source image not specified.");
            if (image.Width > RawSourceImage.Width)
                throw new Exception($"Specified image width can not be larger than source width.");
            if (image.Height > RawSourceImage.Height)
                throw new Exception($"Specified image height can not be larger than source width.");
            if (image.Width > TargetImageContainer.Bitmap.Width)
                throw new Exception($"Specified image width can not be larger than ${TargetImageContainer.Bitmap.Width} pixels.");
            if (image.Height > TargetImageContainer.Bitmap.Height)
                throw new Exception($"Specified image height can not be larger than ${TargetImageContainer.Bitmap.Height} pixels.");
            RawTargetImage = image;
            SimilarityThreshold = similarityThreshold;
            LastMatches.Clear();
            InnerFind();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FindState InnerFind(Int32 scaleDivider = 1) {
            Size sourceSize = new Size(RawSourceImage.Width / scaleDivider, RawSourceImage.Height / scaleDivider);
            Size targetSize = new Size(RawTargetImage.Width / scaleDivider, RawTargetImage.Height / scaleDivider);
            if (targetSize.Width * targetSize.Height >= 100) {
                FindState findState = InnerFind(scaleDivider * 2);
                if (findState != FindState.None) {
                    DrawScaledImages(scaleDivider, sourceSize, targetSize);
                    List<Rectangle> regions = GetRegionsFromMatches(findState, sourceSize);
                    List<Match> matches = SearchForMatches(targetSize, regions, scaleDivider);
                    if (matches.Count > 0) FilterWorstMatches(matches, scaleDivider);
                    if (scaleDivider > 1) MergeNearMatches(matches, scaleDivider);
                    if (scaleDivider == 1) NormalizeMatches(matches);
                    LastMatches = matches;
                    return LastMatches.Count > 0 ? FindState.Sequent : FindState.None;
                } else {
                    return FindState.None;
                }
            } else {
                return FindState.Initial;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawScaledImages(Int32 scaleDivider, Size sourceSize, Size targetSize) {
            if (scaleDivider == 1) {
                SourceImageContainer.Graphics.DrawImageUnscaled(RawSourceImage, Point.Empty);
                TargetImageContainer.Graphics.DrawImageUnscaled(RawTargetImage, Point.Empty);
            } else {
                SourceImageContainer.Graphics.DrawImage(RawSourceImage, new Rectangle(Point.Empty, sourceSize));
                TargetImageContainer.Graphics.DrawImage(RawTargetImage, new Rectangle(Point.Empty, targetSize));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<Rectangle> GetRegionsFromMatches(FindState findState, Size sourceSize) {
            List<Rectangle> result = new List<Rectangle>();
            Rectangle sourceBounds = new Rectangle(Point.Empty, sourceSize);
            if (findState == FindState.Initial) {
                result.Add(sourceBounds);
            } else {
                foreach (Match match in LastMatches) {
                    Point topLeft = new Point();
                    Point bottomRight = new Point();
                    topLeft.Y = (match.Zone.Top - 1) * 2;
                    topLeft.X = (match.Zone.Left - 1) * 2;
                    bottomRight.Y = (match.Zone.Bottom + 1) * 2;
                    bottomRight.X = (match.Zone.Right + 1) * 2;
                    Rectangle region = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                    result.Add(Rectangle.Intersect(region, sourceBounds));
                }
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<Match> SearchForMatches(Size targetSize, List<Rectangle> regions, Int32 scaleDivider) {
            CalculateHashedImageData(SourceImageContainer, SourceHashedImageData, targetSize, regions);
            CalculateHashedImageData(TargetImageContainer, TargetHashedImageData, targetSize, targetSize);
            ConcurrentBag<Match> matches = new ConcurrentBag<Match>();
            Parallel.ForEach(regions, (Rectangle region) => {
                Parallel.For(region.Left, region.Right - targetSize.Width, (Int32 xSource) => {
                    Parallel.For(region.Top, region.Bottom - targetSize.Height, (Int32 ySource) => {
                        Match? match = Compare(xSource, ySource, targetSize, scaleDivider);
                        if (match != null) matches.Add(match.Value);
                    });
                });
            });
            List<Match> result = new List<Match>();
            result.AddRange(matches.ToArray());
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CalculateHashedImageData(ImageContainer imageContainer, HashedImageData[,] hashedImageData, Size patternSize, Size regionSize) {
            List<Rectangle> regions = new List<Rectangle>();
            regions.Add(new Rectangle(Point.Empty, regionSize));
            CalculateHashedImageData(imageContainer, hashedImageData, patternSize, regions);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CalculateHashedImageData(ImageContainer imageContainer, HashedImageData[,] hashedImageData, Size patternSize, List<Rectangle> regions) {
            foreach (Rectangle region in regions) {
                BitmapData bitmapData = imageContainer.Bitmap.LockBits(new Rectangle(Point.Empty, imageContainer.Bitmap.Size), ImageLockMode.ReadOnly, PixelFormat);
                CalculateRGBHSum(bitmapData, hashedImageData, patternSize, region);
                CalculateRGBVSum(bitmapData, hashedImageData, patternSize, region);
                CalculateRGBDiff(bitmapData, hashedImageData, patternSize, region);
                imageContainer.Bitmap.UnlockBits(bitmapData);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CalculateRGBHSum(BitmapData bitmapData, HashedImageData[,] hashedImageData, Size patternSize, Rectangle region) {
            Int32 pixelRowStride = bitmapData.Stride / 3;
            Parallel.For(region.Top, region.Bottom, (Int32 y) => {
                hashedImageData[region.Left, y].HSum.Reset();
                Pixel* pixelBasePtr = (Pixel*)bitmapData.Scan0 + y * pixelRowStride;
                for (Int32 x = region.Left, xMax = region.Left + patternSize.Width - 1; x <= xMax; ++x) {
                    Pixel* pixelPtr = pixelBasePtr + x;
                    fixed (HashedImageData.RGBWeight* hSum = &hashedImageData[region.Left, y].HSum) {
                        hSum->R += pixelPtr->R;
                        hSum->G += pixelPtr->G;
                        hSum->B += pixelPtr->B;
                    }
                }
                for (Int32 x = region.Left, xMax = region.Right - patternSize.Width - 2; x <= xMax; ++x) {
                    Pixel* excessPixelPtr = pixelBasePtr + x;
                    Pixel* newPixelPtr = pixelBasePtr + x + patternSize.Width;
                    fixed (HashedImageData.RGBWeight* hSum = &hashedImageData[x, y].HSum) {
                        fixed (HashedImageData.RGBWeight* nextHSum = &hashedImageData[x + 1, y].HSum) {
                            nextHSum->R = hSum->R - excessPixelPtr->R + newPixelPtr->R;
                            nextHSum->G = hSum->G - excessPixelPtr->G + newPixelPtr->G;
                            nextHSum->B = hSum->B - excessPixelPtr->B + newPixelPtr->B;
                        }
                    }
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CalculateRGBVSum(BitmapData bitmapData, HashedImageData[,] hashedImageData, Size patternSize, Rectangle region) {
            Int32 pixelRowStride = bitmapData.Stride / 3;
            Parallel.For(region.Left, region.Right, (Int32 x) => {
                hashedImageData[x, region.Top].VSum.Reset();
                Pixel* pixelBasePtr = (Pixel*)bitmapData.Scan0 + x;
                for (Int32 y = region.Top, yMax = region.Top + patternSize.Height - 1; y <= yMax; ++y) {
                    Pixel* pixelPtr = pixelBasePtr + y * pixelRowStride;
                    fixed (HashedImageData.RGBWeight* vSum = &hashedImageData[x, region.Top].VSum) {
                        vSum->R += pixelPtr->R;
                        vSum->G += pixelPtr->G;
                        vSum->B += pixelPtr->B;
                    }
                }
                for (Int32 y = region.Top, yMax = region.Bottom - patternSize.Height - 2; y <= yMax; ++y) {
                    Pixel* excessPixelPtr = pixelBasePtr + y * pixelRowStride;
                    Pixel* newPixelPtr = pixelBasePtr + (y + patternSize.Height) * pixelRowStride;
                    fixed (HashedImageData.RGBWeight* vSum = &hashedImageData[x, y].VSum) {
                        fixed (HashedImageData.RGBWeight* nextVSum = &hashedImageData[x, y + 1].VSum) {
                            nextVSum->R = vSum->R - excessPixelPtr->R + newPixelPtr->R;
                            nextVSum->G = vSum->G - excessPixelPtr->G + newPixelPtr->G;
                            nextVSum->B = vSum->B - excessPixelPtr->B + newPixelPtr->B;
                        }
                    }
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CalculateRGBDiff(BitmapData bitmapData, HashedImageData[,] hashedImageData, Size patternSize, Rectangle region) {
            Int32 pixelRowStride = bitmapData.Stride / 3;
            Parallel.For(region.Left, region.Right - 1, (Int32 x) => {
                Pixel* pixelBasePtr = (Pixel*)bitmapData.Scan0 + x;
                for (Int32 y = region.Top, yMax = region.Bottom - 2; y <= yMax; ++y) {
                    Pixel* pixelPtr = pixelBasePtr + y * pixelRowStride;
                    Pixel* neighbourPixel1Ptr = pixelPtr + 1;
                    Pixel* neighbourPixel2Ptr = pixelPtr + bitmapData.Width;
                    fixed (HashedImageData.RGBWeight* diff = &hashedImageData[x, y].Diff) {
                        HashedImageData.RGBWeight temp1 = new HashedImageData.RGBWeight();
                        HashedImageData.RGBWeight temp2 = new HashedImageData.RGBWeight();
                        temp1.R = pixelPtr->R > neighbourPixel1Ptr->R ? pixelPtr->R - neighbourPixel1Ptr->R : neighbourPixel1Ptr->R - pixelPtr->R;
                        temp2.R = pixelPtr->R > neighbourPixel2Ptr->R ? pixelPtr->R - neighbourPixel2Ptr->R : neighbourPixel2Ptr->R - pixelPtr->R;
                        temp1.G = pixelPtr->G > neighbourPixel1Ptr->G ? pixelPtr->G - neighbourPixel1Ptr->G : neighbourPixel1Ptr->G - pixelPtr->G;
                        temp2.G = pixelPtr->G > neighbourPixel2Ptr->G ? pixelPtr->G - neighbourPixel2Ptr->G : neighbourPixel2Ptr->G - pixelPtr->G;
                        temp1.B = pixelPtr->B > neighbourPixel1Ptr->B ? pixelPtr->B - neighbourPixel1Ptr->B : neighbourPixel1Ptr->B - pixelPtr->B;
                        temp2.B = pixelPtr->B > neighbourPixel2Ptr->B ? pixelPtr->B - neighbourPixel2Ptr->B : neighbourPixel2Ptr->B - pixelPtr->B;
                        diff->R = temp1.R + temp2.R + 1;
                        diff->G = temp1.G + temp2.G + 1;
                        diff->B = temp1.B + temp2.B + 1;
                    }
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe Match? Compare(Int32 xSource, Int32 ySource, Size targetSize, Int32 diffStep) {
            RGBDifference VSumDifference = new RGBDifference();
            RGBDifference HSumDifference = new RGBDifference();
            RGBDifference DiffDifference = new RGBDifference();
            for (Int32 xPattern = 0, xPatternMax = targetSize.Width - 1; xPattern <= xPatternMax; ++xPattern)
                fixed (HashedImageData* sourceHashedImageData = &SourceHashedImageData[xSource + xPattern, ySource])
                fixed (HashedImageData* targetHashedImageData = &TargetHashedImageData[xPattern, 0])
                    VSumDifference.AddVSumDifference(sourceHashedImageData, targetHashedImageData);
            for (Int32 yPattern = 0, yPatternMax = targetSize.Height - 1; yPattern <= yPatternMax; ++yPattern)
                fixed (HashedImageData* sourceHashedImageData = &SourceHashedImageData[xSource, ySource + yPattern])
                fixed (HashedImageData* targetHashedImageData = &TargetHashedImageData[0, yPattern])
                    HSumDifference.AddHSumDifference(sourceHashedImageData, targetHashedImageData);
            for (Int32 xPattern = 0, xPatternMax = targetSize.Width - 2; xPattern <= xPatternMax; xPattern += diffStep) {
                for (Int32 yPattern = 0, yPatternMax = targetSize.Height - 2; yPattern <= yPatternMax; yPattern += diffStep) {
                    fixed (HashedImageData* sourceHashedImageData = &SourceHashedImageData[xSource + xPattern, ySource + yPattern])
                    fixed (HashedImageData* targetHashedImageData = &TargetHashedImageData[xPattern, yPattern])
                        DiffDifference.AddDiffDifference(sourceHashedImageData, targetHashedImageData);
                }
            }
            Single similarity;
            Single averageVSumDifference = (VSumDifference.R + VSumDifference.G + VSumDifference.B) / (3 * targetSize.Width);
            Single averageHSumDifference = (HSumDifference.R + HSumDifference.G + HSumDifference.B) / (3 * targetSize.Height);
            Int32 diffTotalArea = ((targetSize.Width - 1) / diffStep) * ((targetSize.Height - 1) / diffStep);
            if (diffTotalArea > 0) {
                Single averageDiffDifference = (DiffDifference.R + DiffDifference.G + DiffDifference.B) / (3 * diffTotalArea);
                similarity = ((averageVSumDifference + averageHSumDifference) * diffStep + averageDiffDifference) / (2 * diffStep + 1);
            } else {
                similarity = (averageVSumDifference + averageHSumDifference) / 2;
            }
            Single stepSimilarityThreshold = SimilarityThreshold * (diffStep == 1 ? 1 : (Single)Math.Pow(0.9275, diffStep));
            if (similarity < stepSimilarityThreshold) return null;
            return new Match(new Rectangle(new Point(xSource, ySource), targetSize), similarity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FilterWorstMatches(List<Match> matches, Int32 scaleDivider) {
            matches.Sort((Match first, Match second) => second.Similarity.CompareTo(first.Similarity));
            if (matches.Count > 64 * scaleDivider) {
                Single threshold = (matches[0].Similarity + matches[matches.Count / 2].Similarity * 2.75f) / 3.75f;
                matches.RemoveAll((Match match) => match.Similarity < threshold);
            } else {
                Single threshold = (matches[0].Similarity * 1.25f + matches[matches.Count - 1].Similarity) / 2.25f;
                matches.RemoveAll((Match match) => match.Similarity < threshold);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MergeNearMatches(List<Match> matches, Int32 scaleDivider) {
            for (Single thresholdBase = 0.9f; thresholdBase < 1; thresholdBase += 0.01f) {
                Single threshold = (Single)Math.Pow(thresholdBase, Math.Pow(scaleDivider, 0.25f));
                for (Int32 n = 0; n < matches.Count; n++) {
                    for (Int32 m = matches.Count - 1; m > n; m--) {
                        if (CheckMatchesIntersect(matches[n], matches[m], threshold)) {
                            Rectangle union = Rectangle.Union(matches[n].Zone, matches[m].Zone);
                            Single similarity = Math.Max(matches[n].Similarity, matches[m].Similarity);
                            matches[n] = new Match(union, similarity);
                            matches.RemoveAt(m);
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void NormalizeMatches(List<Match> matches) {
            for (Int32 n = 0; n < matches.Count; n++) {
                for (Int32 m = matches.Count - 1; m > n; m--) {
                    if (CheckMatchesIntersect(matches[n], matches[m], 0.325f)) {
                        matches[n] = matches[n].Similarity > matches[m].Similarity ? matches[n] : matches[m];
                        matches.RemoveAt(m);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Boolean CheckMatchesIntersect(Match first, Match second, Single threshold) {
            Rectangle union = Rectangle.Union(first.Zone, second.Zone);
            Rectangle intersection = Rectangle.Intersect(first.Zone, second.Zone);
            Int32 firstZoneArea = first.Zone.Width * first.Zone.Height;
            Int32 secondZoneArea = second.Zone.Width * second.Zone.Height;
            Int32 unionArea = union.Width * union.Height;
            Int32 intersectionArea = intersection.Width * intersection.Height;
            return (Single)intersectionArea / (Single)Math.Min(firstZoneArea, secondZoneArea) > threshold;
        }

    }

}