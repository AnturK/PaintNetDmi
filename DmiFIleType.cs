using PaintDotNet;
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using PaintDotNet.Rendering;
using SixLabors.ImageSharp.Processing;

namespace PaintNetDmi
{


    public sealed class DmiFileType : FileType
    {

        private static class MetadataNames
        {
            public const string DMI_StateName = "DMI_StateName";
            public const string DMI_RawData = "DMI_RawData";
        };

        public DmiFileType() : base("DMI File", new FileTypeOptions() { LoadExtensions = new string[] { ".dmi" }, SaveExtensions = new string[] { ".dmi" }, SupportsLayers = true })
        {
        }

        /// <summary>
        /// Calculates offsets for the given frame number
        /// </summary>
        /// <param name="frameCount"></param>
        /// <param name="totalWidth"></param>
        /// <param name="totalHeight"></param>
        /// <param name="frameWidth"></param>
        /// <param name="frameHeight"></param>
        /// <returns></returns>
        private Point CalculateOffset(int frameCount, int totalWidth, int totalHeight, int frameWidth, int frameHeight)
        {
            var framesPerRow = totalWidth / frameWidth;
            var row = frameCount / framesPerRow;
            var col = frameCount % framesPerRow;
            var xOffset = col * frameWidth;
            var yOffset = row * frameHeight;
            return new Point(xOffset, yOffset);
        }

        protected override Document OnLoad(Stream input)
        {
            byte[] rawData;
            using (MemoryStream ms = new MemoryStream())
            {
                input.CopyTo(ms);
                rawData = ms.ToArray();
            }
            var dataString = Convert.ToBase64String(rawData);

            DMISharp.DMIFile dmi;
            using (var ms = new MemoryStream(rawData))
            {
                dmi = new DMISharp.DMIFile(ms);
            }

            var numFrames = 0;
            foreach (var state in dmi.States)
            {
                for (int frame = 0; frame < state.Frames; frame++)
                {
                    for (int dir = 0; dir < state.Dirs; dir++)
                    {
                        numFrames += 1;
                    }
                }
            }

            Image rawImage = Image.FromStream(new MemoryStream(rawData));
            Surface rawSurface = Surface.CopyFromGdipImage(rawImage);

            // Document  - stuff raw data into document metadata to reuse when saving.
            Document doc = new Document(rawImage.Width, rawImage.Height);
            doc.Metadata.SetUserValue(MetadataNames.DMI_RawData, dataString);
            // Each state gets a separate layer
            var frameNumber = 0;
            foreach (var state in dmi.States)
            {
                BitmapLayer stateLayer = new BitmapLayer(doc.Size) { Name = state.Name };

                for (int frame = 0; frame < state.Frames; frame++)
                {
                    for (int dir = 0; dir < state.Dirs; dir++)
                    {
                        var offset = CalculateOffset(frameNumber, stateLayer.Width, stateLayer.Height, dmi.Metadata.FrameWidth, dmi.Metadata.FrameHeight);
                        Point2Int32 dstOffset = new Point2Int32(offset.X, offset.Y);
                        RectInt32 srcRect = new RectInt32(dstOffset, new SizeInt32(dmi.Metadata.FrameWidth, dmi.Metadata.FrameHeight));
                        try
                        {
                            stateLayer.Surface.CopySurface(rawSurface, dstOffset, srcRect);
                        }
                        catch (Exception)
                        {
                            throw new Exception($"Failed copying surface for frameNumber:{frameNumber} frameWidth:{dmi.Metadata.FrameWidth} frameHeight:{dmi.Metadata.FrameHeight}");
                        }

                        frameNumber += 1;
                    }
                }

                stateLayer.Metadata.SetUserValue(MetadataNames.DMI_StateName, state.Name);
                doc.Layers.Add(stateLayer);
            }
            return doc;
        }

        protected override void OnSave(Document input, Stream output, SaveConfigToken token, Surface scratchSurface, ProgressEventHandler callback)
        {
            /// Up to date raw data.
            using (RenderArgs renderArgs = new RenderArgs(scratchSurface))
            {
                input.Render(renderArgs, true);
            }
            var bitmap = scratchSurface.CreateAliasedBitmap();
            byte[] rawData;
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Position = 0;
                rawData = memoryStream.ToArray();
            }

            SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> fullImage = SixLabors.ImageSharp.Image.Load(rawData);

            var originalMetaData = input.Metadata.GetUserValue(MetadataNames.DMI_RawData);
            DMISharp.DMIFile dmi;
            if (!String.IsNullOrEmpty(originalMetaData))
            {
                //Modyfing loaded DMI - preserve states info, apply frame changes and state name changes
                byte[] originalRawData = Convert.FromBase64String(originalMetaData);
                dmi = new DMISharp.DMIFile(new MemoryStream(originalRawData));

                /// Update every state image from current document
                var frameNumber = 0;
                foreach (var state in dmi.States)
                {
                    for (int frame = 0; frame < state.Frames; frame++)
                    {
                        for (int dir = 0; dir < state.Dirs; dir++)
                        {
                            var offset = CalculateOffset(frameNumber, input.Width, input.Height, dmi.Metadata.FrameWidth, dmi.Metadata.FrameHeight);
                            Point2Int32 dstOffset = new Point2Int32(offset.X, offset.Y);
                            RectInt32 srcRect = new RectInt32(dstOffset, new SizeInt32(dmi.Metadata.FrameWidth, dmi.Metadata.FrameHeight));
                            var newFrame = fullImage.Clone(x => x.Crop(new SixLabors.ImageSharp.Rectangle(offset.X, offset.Y, dmi.Metadata.FrameWidth, dmi.Metadata.FrameHeight)));
                            state.SetFrame(newFrame, (DMISharp.StateDirection)dir, frame);
                            frameNumber += 1;
                        }
                    }
                }
                ///Update state names if they changed
                foreach (var layer in input.Layers)
                {
                    var stateName = layer.Metadata.GetUserValue(MetadataNames.DMI_StateName);
                    if (!String.IsNullOrEmpty(stateName))
                    {
                        var originalState = dmi.States.Where(state => state.Name == stateName).FirstOrDefault();
                        originalState.Name = layer.Name;
                    }
                }
            }
            else
            {
                //Creating DMI from scratch - generate icon state per layer, assume square frames, single dir and no animations
                var framesPerLine = (int)Math.Ceiling(Math.Sqrt(input.Layers.Count));
                var frameWidth = input.Width / framesPerLine;
                var frameHeight = input.Height / framesPerLine;
                dmi = new DMISharp.DMIFile(frameWidth, frameHeight);
                var stateCounter = 0;
                foreach (var layer in input.Layers)
                {
                    var offset = CalculateOffset(stateCounter, input.Width, input.Height, frameWidth, frameHeight);
                    var newFrame = fullImage.Clone(x => x.Crop(new SixLabors.ImageSharp.Rectangle(offset.X, offset.Y, frameWidth, frameHeight)));
                    var newState = new DMISharp.DMIState(layer.Name, DMISharp.DirectionDepth.One, 1, frameWidth, frameHeight);
                    newState.SetFrame(newFrame, 0);
                    dmi.AddState(newState);
                    stateCounter += 1;
                }
            }
            dmi.Save(output);
        }
    }

    public sealed class DmiFileTypeFactory : IFileTypeFactory
    {
        public FileType[] GetFileTypeInstances()
        {
            return new FileType[] { new DmiFileType() };
        }
    }
}
