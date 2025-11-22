# m4b-merge

m4b-merge is a simple command line tool for merging audiobook files into a single m4b file.

## Features

- Automatic re-encoding (when necessary, avoids where possible)
- Chapter generation
- Supports individual input files in addition to existing m4b files
- Supports mp3, aac and flac output encoding

## Installation

**To use m4b-merge you need to have ffmpeg installed and accessible on your PATH.**

m4b-merge binaries can be downloaded from [Releases](https://github.com/jamerst/m4b-merge/releases).

## Usage

```
USAGE:
    m4b-merge [paths] [OPTIONS]

ARGUMENTS:
    [paths]    Files to process

OPTIONS:
    -h, --help                    Prints help information
    -o, --output                  Output file path
    -c, --codec                   Output file audio codec override (aac|mp3|flac)
    -b, --bitrate                 Output file bitrate (in kb) - required if lossy codec specified
    -m, --metadata <KEY=VALUE>    Additional metadata values to set on output file (note: custom tag keys are not supported, only those known to ffmpeg)
        --debug                   Enable debugging output mode
        --version                 Print version and exit
```

The only required option is `--output`. Files are merged in the order provided as arguments.

### Examples

```shell
m4b-merge *.mp3 -o output.m4b
# merge all mp3 files in the current directory into output.m4b
```

```shell
m4b-merge 1.mp3 2.m4a 3.flac 4.ogg 5.m4b -o output.m4b
# merge the listed files into output.m4b, automatically re-encoding them to a common codec
```

```shell
m4b-merge *.mp3 -o output.m4b -c aac -b 128
# merge all mp3 files in the current directory into output.m4b, re-encoding as 128k AAC
```

```shell
m4b-merge *.mp3 -o output.m4b -m title="Title of the book" -m artist="Author of the book"
# merge all mp3 files in the current directory into output.m4b, adding title and artist metadata
```

### Codec

An output codec can be specified with the `--codec` option. If not specified m4b-merge will use the same codec as the input files (or the most common codec if the input files use multiple codecs).

Files will only be re-encoded if the codec does not match the output codec. Otherwise the original files will be concatenated together without re-encoding.

If specifying a lossy codec (e.g. mp3 or aac) you also need to specify a bitrate with the `--bitrate` option. Note that files already using the output codec won't be re-encoded at this bitrate - the bitrate is only used when re-encoding files that don't match the output codec.

### Metadata

m4b-merge also allows adding basic metadata to the output file with the `--metadata` option. Unfortunately ffmpeg support for mp4 metadata is limited, so you can only add a [subset of metadata keys](https://blog.travisflix.com/supported-mp4-metadata-keys-with-ffmpeg/).