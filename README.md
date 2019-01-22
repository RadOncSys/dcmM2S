# dcmM2S

DICOM converter from multiframe format to single image files.

Programm uses Fellow Oak DICOM fo-dicom library (https://github.com/fo-dicom/fo-dicom).

## Usage:

    dcmM2S input_dir output_dir

### where

    input_dir  - folder name with files to be converted
    output_dir - folder name where converted data will be written

Input folder will be scanned on the full depth.

Output data will be organized under the hierarchy:

    output_dir/study_label/series_label/*

## Example

    dcmM2S c:/tmp/study_in c:/tmp/study_out

## Notes

Program copies all dicom tags from original file except that we know in advance needed to decribse frames. Dicom items from that frames description sequence (0x5200, 0x9230) are copied to the root of output file dataset. Each exported file contains only its own frame pixel data.
