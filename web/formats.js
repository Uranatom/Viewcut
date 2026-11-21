const formats = {
  mp4: ['mp4', 'video/mp4'], mov: ['mov', 'video/quicktime'], m4v: ['mp4', 'video/mp4'],
  mkv: ['matroska', 'video/x-matroska'], webm: ['webm', 'video/webm'], avi: ['avi', 'video/x-msvideo'],
  ts: ['mpegts', 'video/mp2t'], m2ts: ['mpegts', 'video/mp2t'],
};

export function videoFormat(name) {
  const extension = /\.([^.]+)$/.exec(name)?.[1].toLowerCase();
  if (!Object.hasOwn(formats, extension)) return null;
  const [muxer, mime] = formats[extension];
  return { extension, muxer, mime };
}

export const videoAccept = ['video/*', ...Object.keys(formats).map(extension => '.' + extension)].join(',');
