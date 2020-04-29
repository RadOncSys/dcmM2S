// THIS CODE BELONGS TO Radiation Oncology Intellectual Systems and Services LLC
// Copyright (c) 2015, ROISS LLC. All rights reserved
//
// Author: Gennady Gorlachev (ggorlachev@roiss.ru)
//---------------------------------------------------------------------------
// Programm uses Fellow Oak DICOM fo-dicom library
// (https://github.com/fo-dicom/fo-dicom)")
//---------------------------------------------------------------------------

using Dicom;
using Dicom.Imaging;
using Dicom.Imaging.Codec;
using System;
using System.IO;
using System.Linq;

namespace dcmM2S
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("    DICOM converter from multiframe format to single image files (https://github.com/RadOncSys/dcmM2S)");
            Console.WriteLine("    Programm uses Fellow Oak DICOM fo-dicom library (https://github.com/fo-dicom/fo-dicom)");
            Console.WriteLine("    Copyright ROISS LLC (http://roiss.ru)");
            Console.WriteLine();

            if (args.Length != 2)
            {
                Console.WriteLine("    Usage:");
                Console.WriteLine("        dcmM2S input_dir output_dir");
                Console.WriteLine();
                Console.WriteLine("    where");
                Console.WriteLine("        input_dir  - folder name with files to be converted");
                Console.WriteLine("        output_dir - folder name where converted data will be written");
                Console.WriteLine();
                Console.WriteLine("        Input folder will be scanned on the full depth.");
                Console.WriteLine("        Output data will be organized under the hierarchy:");
                Console.WriteLine("            output_dir/study_label/series_label/*");
                Console.WriteLine();
                Console.WriteLine("    Example");
                Console.WriteLine("        dcmM2S c:/tmp/study_in c:/tmp/study_out");
                return;
            }

            try
            {
                string inputRoot = args[0];
                string outRoot = args[1];

                if (outRoot.ElementAt(outRoot.Length - 1) != '/')
                    outRoot += '/';

                if (inputRoot.ElementAt(inputRoot.Length - 1) != '/')
                    inputRoot += '/';

                // All work will be done recursively
                ConvertPath(inputRoot, outRoot);

                Console.WriteLine("All data exported to the folder: " + outRoot);
                Console.WriteLine();
                Console.WriteLine("Success!");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Convert ol files recursively
        /// </summary>
        /// <param name="path">folder path to convert</param>
        /// <param name="outRoot">root folder path to export data</param>
        static void ConvertPath(string path, string outRoot)
        {
            DirectoryInfo dir = new DirectoryInfo(path);
            FileInfo[] files = dir.GetFiles();

            foreach (FileInfo file in files)
            {
                ConvertFile(file.FullName, outRoot);
            }

            string[] subdirectories = Directory.GetDirectories(dir.FullName);
            foreach (string subPath in subdirectories)
            {
                ConvertPath(subPath, outRoot);
            }
        }

        /// <summary>
        /// Convert single file to a set of files acording the number of frames
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="outRoot"></param>
        [Obsolete]
        static void ConvertFile(string fileFullName, string outRoot)
        {
            try
            {
                Console.WriteLine("Reading file: " + fileFullName);

                DicomFile file = DicomFile.Open(fileFullName);
                ushort nframes = file.Dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, (ushort)1);

                // If file ocoursanally has only one frame we copy it with new name according to converter policy.
                if (nframes <= 1)
                {
                    Console.WriteLine("Copying");
                    var fname = outRoot + CreateImageFileName(file);
                    CreatePathToFileIfNeeded(fname);

                    // Change Transfer Syntax to most usual for radiation therapy format
                    var uncompressedFile = file.Clone(DicomTransferSyntax.ExplicitVRLittleEndian);
                    uncompressedFile.Save(fname);
                }
                else
                {
                    Console.WriteLine("Converting");

                    //
                    // The most Important part !!!
                    //
                    // Here is going multiframe file spliting logic.
                    // We copy all dicom tags from original file except that we know in advance decribse frames.
                    // Than we add image position, orientation and number, taken from frames description sequences.
                    // Than we replace pixel data with particular frame.
                    //

                    // Clone dicom object, which we will use as a template
                    var templateObject = file.Clone();

                    // Base for instance uids
                    var mediaSopInstUid = file.FileMetaInfo.GetSingleValueOrDefault(DicomTag.MediaStorageSOPInstanceUID, string.Empty);
                    var sopInstUid = file.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty);

                    // One frame in output
                    templateObject.Dataset.AddOrUpdate(DicomTag.NumberOfFrames, 1);

                    // Sequence with data to copy to output image
                    if (!file.Dataset.TryGetSequence(DicomTag.PerFrameFunctionalGroupsSequence, out DicomSequence mfseq))
                        throw new ApplicationException("Shared Functional Groups Sequence (0x5200, 0x9230) is expected to get sencitive information");

                    // Remove multiframe Shared Functional Groups Sequence
                    templateObject.Dataset.Remove(DicomTag.PerFrameFunctionalGroupsSequence);

                    // Original file pixel data
                    var oldPixelData = DicomPixelData.Create(file.Dataset, false);

                    // Frames iteration
                    for (ushort i = 0; i < nframes; i++)
                    {
                        // Instance UID is formed from original file plus dot and frame number
                        templateObject.FileMetaInfo.AddOrUpdate(DicomTag.MediaStorageSOPInstanceUID, mediaSopInstUid + '.' + i.ToString());
                        templateObject.Dataset.AddOrUpdate(DicomTag.SOPInstanceUID, sopInstUid + '.' + i.ToString());
                        templateObject.Dataset.AddOrUpdate(DicomTag.InstanceNumber, i+1);

                        // Technical information
                        var sqitem = mfseq.Items[i];
                        foreach (var element in sqitem)
                        {
                            if (element.ValueRepresentation == DicomVR.SQ)
                            {
                                var sq = element as DicomSequence;
                                foreach (var ds in sq)
                                    ds.CopyTo(templateObject.Dataset);
                            }
                            else
                            {
                                templateObject.Dataset.AddOrUpdate(element);
                            }
                        }

                        // Pixel data
                        var pixelData = DicomPixelData.Create(templateObject.Dataset, true);
                        var framepixels = oldPixelData.GetFrame(i);
                        pixelData.AddFrame(framepixels);

                        // Save to file
                        var fname = outRoot + CreateImageFileName(templateObject);
                        CreatePathToFileIfNeeded(fname);
                        templateObject.Save(fname);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error in processing file: " + fileFullName + " / " + e.Message);
            }
        }

        /// <summary>
        /// File name for image file, based on study? series and image number
        /// </summary>
        /// <param name="image"></param>
        static string CreateImageFileName(DicomFile file)
        {
            var patName = file.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "Unknown");
            var studyDate = file.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty);
            var series_time = file.Dataset.GetSingleValueOrDefault(DicomTag.SeriesTime, string.Empty);
            var seriesDescription = file.Dataset.GetSingleValueOrDefault(DicomTag.SeriesDescription, "Unknown");
            int seriesNumber = Convert.ToInt16(file.Dataset.GetSingleValueOrDefault(DicomTag.SeriesNumber, "0"));
            int imageNumber = Convert.ToInt16(file.Dataset.GetSingleValueOrDefault(DicomTag.InstanceNumber, "-1"));
            if (imageNumber < 0)
                throw new ApplicationException("Dicom file must have instance number");

            var fname = string.Format("{0}_{1}/{2}_{3}_{4}/img_{5:0000}.dcm", 
                patName, studyDate, seriesNumber, seriesDescription, series_time, imageNumber);

            return CreateValidFileName(fname);
        }

        static void CreatePathToFileIfNeeded(string fname)
        {
            string[] dirs = fname.Split(new Char[] { '/', '\\' });
            if (dirs.Length <= 1) return;
            string currentDir = dirs[0];
            for (int i = 1; i < dirs.Length; i++)
            {
                if (!System.IO.Directory.Exists(currentDir))
                    System.IO.Directory.CreateDirectory(currentDir);
                currentDir += '/' + dirs[i];
            }
        }

        static string CreateValidFileName(string fname)
        {
            var a = fname.ToArray();
            for(int i=0; i< a.Length; i++)
            {
                var c = a[i];
                if (c == '^' || c == '?') a[i] = '_';

            }
            return new string(a);
        }
    }
}
