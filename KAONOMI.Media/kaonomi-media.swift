// kaonomi-media: the videos of /video in the chat, with AVFoundation (the decoders of macOS, by the hardware).
//
//   kaonomi-media probe <video>                                   what the video is (JSON)
//   kaonomi-media card <video> <out.jpg> <width>                  what it is (JSON) and a thumbnail at 10 %
//   kaonomi-media thumb <video> <seconds> <out.jpg> <width> [exact]   the picture at that time (width 0: full size)
//   kaonomi-media strip <video> <count> <out.jpg> <height>        a strip of pictures across the video (the timeline)
//   kaonomi-media sheet <video> <start> <end> <count> <out.jpg> <cell width>
//                                                                 the pictures of a segment on a sheet, numbered with their time
//   kaonomi-media audio <video> <start> <end> <out.wav>           the sound of a segment, 16 kHz mono 16 bits
//   kaonomi-media play <video> <start> <width>                    the video and its sound from <start>, on the standard output
//
// The stream of "play": packets of a type (1 byte: I info, V video, A audio, E end), a time in seconds (Float64,
// little endian), a length (UInt32, little endian) and the data: I is JSON, V a JPEG, A interleaved Float32 stereo at
// 44100 Hz. Pictures come at 30 per second at most, in the order of the time with the sound.
// What AVFoundation cannot read (webm, mkv, avi, wmv, flv…) goes through ffmpeg and ffprobe, when they are next to
// this tool (~/kaonomi/video/bin): the same commands, the same stream.
// Built by the chat with: swiftc -O -swift-version 5 kaonomi-media.swift -o kaonomi-media

import AVFoundation
import CoreImage
import CoreText
import Foundation
import ImageIO
import UniformTypeIdentifiers

signal(SIGPIPE, SIG_IGN)

func fail(_ message: String) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(1)
}

let arguments = CommandLine.arguments
guard arguments.count >= 3 else {
    fail("usage: kaonomi-media probe|card|thumb|strip|sheet|audio|play <video> …")
}
let command = arguments[1]
let asset = AVURLAsset(url: URL(fileURLWithPath: arguments[2]), options: [AVURLAssetPreferPreciseDurationAndTimingKey: true])

func argument(_ index: Int, _ fallback: Double = 0) -> Double { index < arguments.count ? Double(arguments[index]) ?? fallback : fallback }
func text(_ index: Int) -> String { index < arguments.count ? arguments[index] : "" }
func time(_ seconds: Double) -> CMTime { CMTime(seconds: max(0, seconds), preferredTimescale: 600) }

func clock(_ seconds: Double) -> String {
    let total = max(0, seconds)
    let minutes = Int(total) / 60
    let rest = total - Double(minutes * 60)
    return minutes >= 60 ? String(format: "%d:%02d:%04.1f", minutes / 60, minutes % 60, rest) : String(format: "%02d:%04.1f", minutes, rest)
}

func jpeg(_ image: CGImage, quality: Double = 0.84) -> Data {
    let data = NSMutableData()
    guard let destination = CGImageDestinationCreateWithData(data, UTType.jpeg.identifier as CFString, 1, nil) else { return Data() }
    CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: quality] as CFDictionary)
    CGImageDestinationFinalize(destination)
    return data as Data
}

func write(_ data: Data, to path: String) {
    do { try data.write(to: URL(fileURLWithPath: path)) } catch { fail("could not write \(path): \(error.localizedDescription)") }
}

func printJson(_ value: [String: Any]) {
    let data = (try? JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])) ?? Data("{}".utf8)
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data("\n".utf8))
}

// ── What the video is ──

func probe() async -> [String: Any] {
    let duration = (try? await asset.load(.duration))?.seconds ?? 0
    let playable = (try? await asset.load(.isPlayable)) ?? false
    let videos = (try? await asset.loadTracks(withMediaType: .video)) ?? []
    let sounds = (try? await asset.loadTracks(withMediaType: .audio)) ?? []
    var width = 0, height = 0
    var fps = 0.0
    if let video = videos.first {
        let size = (try? await video.load(.naturalSize)) ?? .zero
        let transform = (try? await video.load(.preferredTransform)) ?? .identity
        let shown = CGRect(origin: .zero, size: size).applying(transform)
        width = Int(abs(shown.width).rounded())
        height = Int(abs(shown.height).rounded())
        fps = Double((try? await video.load(.nominalFrameRate)) ?? 0)
    }
    return ["duration": duration.isFinite ? duration : 0, "width": width, "height": height, "fps": fps,
            "hasVideo": !videos.isEmpty, "hasAudio": !sounds.isEmpty, "playable": playable && !videos.isEmpty]
}

func generator(width: Int, exact: Bool) -> AVAssetImageGenerator {
    let generator = AVAssetImageGenerator(asset: asset)
    generator.appliesPreferredTrackTransform = true
    if width > 0 {
        generator.maximumSize = CGSize(width: width, height: width * 4)
    }
    let tolerance = exact ? CMTime.zero : time(0.4)
    generator.requestedTimeToleranceBefore = tolerance
    generator.requestedTimeToleranceAfter = tolerance
    return generator
}

func picture(_ generator: AVAssetImageGenerator, at seconds: Double) async -> CGImage? {
    try? await generator.image(at: time(seconds)).image
}

func duration() async -> Double {
    let seconds = (try? await asset.load(.duration))?.seconds ?? 0
    return seconds.isFinite ? seconds : 0
}

// ── Drawing ──

func context(_ width: Int, _ height: Int) -> CGContext {
    guard let context = CGContext(data: nil, width: max(1, width), height: max(1, height), bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else {
        fail("no drawing context")
    }
    context.interpolationQuality = .high
    return context
}

/// A label on a dark box, its bottom left corner at <at> (the origin of Core Graphics is at the bottom).
func label(_ context: CGContext, _ value: String, at: CGPoint, size: CGFloat) {
    let font = CTFontCreateWithName("Helvetica-Bold" as CFString, size, nil)
    let attributes: [NSAttributedString.Key: Any] = [
        NSAttributedString.Key(kCTFontAttributeName as String): font,
        NSAttributedString.Key(kCTForegroundColorAttributeName as String): CGColor(red: 1, green: 1, blue: 1, alpha: 1)
    ]
    let line = CTLineCreateWithAttributedString(NSAttributedString(string: value, attributes: attributes))
    let width = CTLineGetTypographicBounds(line, nil, nil, nil)
    context.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 0.68))
    context.fill(CGRect(x: at.x, y: at.y, width: CGFloat(width) + size, height: size * 1.55))
    context.textPosition = CGPoint(x: at.x + size / 2, y: at.y + size * 0.42)
    CTLineDraw(line, context)
}

// ── The stream of play ──

func packet(_ type: UInt8, _ seconds: Double, _ data: Data) {
    var header = Data([type])
    var pts = seconds.bitPattern.littleEndian
    var length = UInt32(data.count).littleEndian
    withUnsafeBytes(of: &pts) { header.append(contentsOf: $0) }
    withUnsafeBytes(of: &length) { header.append(contentsOf: $0) }
    do {
        try FileHandle.standardOutput.write(contentsOf: header)
        if !data.isEmpty {
            try FileHandle.standardOutput.write(contentsOf: data)
        }
    } catch {
        exit(0) // the chat stopped reading: the window closed, or it seeks elsewhere
    }
}

func bytes(_ buffer: CMSampleBuffer) -> Data {
    guard let block = CMSampleBufferGetDataBuffer(buffer) else { return Data() }
    let length = CMBlockBufferGetDataLength(block)
    var data = Data(count: length)
    data.withUnsafeMutableBytes { raw in
        _ = CMBlockBufferCopyDataBytes(block, atOffset: 0, dataLength: length, destination: raw.baseAddress!)
    }
    return data
}

func orientation(_ transform: CGAffineTransform) -> CGImagePropertyOrientation {
    let degrees = Int((atan2(transform.b, transform.a) * 180 / .pi).rounded())
    switch (degrees + 360) % 360 {
    case 90: return .right
    case 180: return .down
    case 270: return .left
    default: return .up
    }
}

func play(from start: Double, width: Int) async {
    let videos = (try? await asset.loadTracks(withMediaType: .video)) ?? []
    let sounds = (try? await asset.loadTracks(withMediaType: .audio)) ?? []
    guard let reader = try? AVAssetReader(asset: asset) else { fail("the video cannot be read") }
    reader.timeRange = CMTimeRange(start: time(start), duration: .positiveInfinity)
    var videoOutput: AVAssetReaderTrackOutput?
    var turn = CGImagePropertyOrientation.up
    if let video = videos.first {
        let output = AVAssetReaderTrackOutput(track: video, outputSettings: [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA])
        if reader.canAdd(output) {
            reader.add(output)
            videoOutput = output
        }
        turn = orientation((try? await video.load(.preferredTransform)) ?? .identity)
    }
    var audioOutput: AVAssetReaderAudioMixOutput?
    if !sounds.isEmpty {
        let output = AVAssetReaderAudioMixOutput(audioTracks: sounds, audioSettings: [
            AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: 44100, AVNumberOfChannelsKey: 2,
            AVLinearPCMBitDepthKey: 32, AVLinearPCMIsFloatKey: true, AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false
        ])
        if reader.canAdd(output) {
            reader.add(output)
            audioOutput = output
        }
    }
    guard reader.startReading() else { fail("the video cannot be read: \(reader.error?.localizedDescription ?? "?")") }
    let info: [String: Any] = ["start": start, "hasAudio": audioOutput != nil, "hasVideo": videoOutput != nil, "rate": 44100, "channels": 2]
    packet(UInt8(ascii: "I"), start, (try? JSONSerialization.data(withJSONObject: info)) ?? Data())

    let images = CIContext(options: [.cacheIntermediates: false])
    var nextVideo = videoOutput?.copyNextSampleBuffer()
    var nextAudio = audioOutput?.copyNextSampleBuffer()
    var shown = -1.0
    while nextVideo != nil || nextAudio != nil {
        let videoTime = nextVideo.map { CMSampleBufferGetPresentationTimeStamp($0).seconds } ?? .infinity
        let audioTime = nextAudio.map { CMSampleBufferGetPresentationTimeStamp($0).seconds } ?? .infinity
        if videoTime <= audioTime, let buffer = nextVideo {
            if videoTime - shown >= 0.031, let pixels = CMSampleBufferGetImageBuffer(buffer) {
                var image = CIImage(cvPixelBuffer: pixels).oriented(turn)
                image = image.transformed(by: CGAffineTransform(translationX: -image.extent.origin.x, y: -image.extent.origin.y))
                if width > 0 && image.extent.width > CGFloat(width) {
                    let scale = CGFloat(width) / image.extent.width
                    image = image.transformed(by: CGAffineTransform(scaleX: scale, y: scale))
                }
                if let frame = images.createCGImage(image, from: image.extent.integral) {
                    packet(UInt8(ascii: "V"), videoTime, jpeg(frame, quality: 0.8))
                    shown = videoTime
                }
            }
            nextVideo = videoOutput?.copyNextSampleBuffer()
        } else if let buffer = nextAudio {
            packet(UInt8(ascii: "A"), audioTime, bytes(buffer))
            nextAudio = audioOutput?.copyNextSampleBuffer()
        }
    }
    packet(UInt8(ascii: "E"), 0, Data())
}

// ── ffmpeg: what AVFoundation cannot read ──

let tools = URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent()
let ffmpeg = tools.appendingPathComponent("ffmpeg").path
let ffprobe = tools.appendingPathComponent("ffprobe").path
let hasFfmpeg = FileManager.default.isExecutableFile(atPath: ffmpeg) && FileManager.default.isExecutableFile(atPath: ffprobe)
let videoPath = arguments[2]

/// Runs a program; what it wrote, or nil when it failed.
func run(_ program: String, _ parameters: [String]) -> Data? {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: program)
    process.arguments = parameters
    let output = Pipe()
    process.standardOutput = output
    process.standardError = FileHandle.nullDevice
    do { try process.run() } catch { return nil }
    let data = output.fileHandleForReading.readDataToEndOfFile()
    process.waitUntilExit()
    return process.terminationStatus == 0 ? data : nil
}

func number(_ value: Double) -> String { String(format: "%.3f", max(0, value)) }

/// What ffprobe says of the video, as probe says it ("backend": "ffmpeg").
func ffProbe() -> [String: Any]? {
    guard hasFfmpeg, let data = run(ffprobe, ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", videoPath]),
          let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
    let streams = root["streams"] as? [[String: Any]] ?? []
    let video = streams.first { ($0["codec_type"] as? String) == "video" && (($0["disposition"] as? [String: Any])?["attached_pic"] as? Int ?? 0) == 0 }
    let audio = streams.contains { ($0["codec_type"] as? String) == "audio" }
    let format = root["format"] as? [String: Any] ?? [:]
    var duration = Double(format["duration"] as? String ?? "") ?? 0
    if duration <= 0, let seconds = Double(video?["duration"] as? String ?? "") { duration = seconds }
    var width = video?["width"] as? Int ?? 0
    var height = video?["height"] as? Int ?? 0
    var rotation = 0.0
    if let sides = video?["side_data_list"] as? [[String: Any]], let turn = sides.compactMap({ $0["rotation"] as? Double ?? ($0["rotation"] as? Int).map(Double.init) }).first {
        rotation = turn
    } else if let tag = (video?["tags"] as? [String: Any])?["rotate"] as? String, let turn = Double(tag) {
        rotation = turn
    }
    if Int(abs(rotation).rounded()) % 180 == 90 { swap(&width, &height) }
    var fps = 0.0
    let rate = (video?["avg_frame_rate"] as? String ?? "0/1").split(separator: "/").compactMap { Double($0) }
    if rate.count == 2, rate[1] > 0 { fps = rate[0] / rate[1] }
    return ["duration": duration, "width": width, "height": height, "fps": fps, "hasVideo": video != nil, "hasAudio": audio,
            "playable": video != nil && duration > 0, "backend": "ffmpeg"]
}

/// The picture at <seconds> through ffmpeg (turned as shown), <width> or <height> wide or high (0: full size).
func ffPicture(_ seconds: Double, width: Int = 0, height: Int = 0) -> CGImage? {
    var parameters = ["-v", "error", "-ss", number(seconds), "-i", videoPath, "-frames:v", "1", "-an"]
    if width > 0 { parameters += ["-vf", "scale='min(\(width),iw)':-2"] } else if height > 0 { parameters += ["-vf", "scale=-2:\(height)"] }
    parameters += ["-f", "image2pipe", "-c:v", "mjpeg", "-q:v", "2", "-"]
    guard let data = run(ffmpeg, parameters), !data.isEmpty,
          let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
    return CGImageSourceCreateImageAtIndex(source, 0, nil)
}

/// Pictures at several times through ffmpeg, 6 at once (in the order of the times; a missing one is left out).
func ffPictures(_ times: [Double], width: Int = 0, height: Int = 0) -> [(CGImage, Double)] {
    var found = [CGImage?](repeating: nil, count: times.count)
    let lock = NSLock()
    let queue = OperationQueue()
    queue.maxConcurrentOperationCount = 6
    for (index, at) in times.enumerated() {
        queue.addOperation {
            let image = ffPicture(at, width: width, height: height)
            lock.lock(); found[index] = image; lock.unlock()
        }
    }
    queue.waitUntilAllOperationsAreFinished()
    return zip(found, times).compactMap { image, at in image.map { ($0, at) } }
}

/// A queue between a reader and the stream: the reader waits while it is full, the stream while it is empty.
final class Feed: @unchecked Sendable {
    private var items: [(Double, Data)] = []
    private var finished = false
    private let condition = NSCondition()
    private let capacity: Int

    init(capacity: Int) { self.capacity = capacity }

    func put(_ item: (Double, Data)) {
        condition.lock()
        while items.count >= capacity { condition.wait() }
        items.append(item)
        condition.broadcast()
        condition.unlock()
    }

    func finish() {
        condition.lock(); finished = true; condition.broadcast(); condition.unlock()
    }

    /// The next item (waiting for it), or nil at the end.
    func peek() -> (Double, Data)? {
        condition.lock()
        while items.isEmpty && !finished { condition.wait() }
        let first = items.first
        condition.unlock()
        return first
    }

    func pop() {
        condition.lock(); if !items.isEmpty { items.removeFirst() }; condition.broadcast(); condition.unlock()
    }
}

/// Reads exactly <count> bytes (fewer at the end of the stream).
func read(_ handle: FileHandle, _ count: Int) -> Data {
    var data = Data()
    while data.count < count {
        let part = handle.readData(ofLength: count - data.count)
        if part.isEmpty { break }
        data.append(part)
    }
    return data
}

/// The stream of play through ffmpeg: one ffmpeg for the pictures (raw RGB at 30 per second, made JPEG here), one for
/// the sound (Float32 stereo at 44100 Hz), merged in the order of the time.
func ffPlay(from start: Double, width maxWidth: Int, probe found: [String: Any]) {
    let sourceWidth = found["width"] as? Int ?? 0, sourceHeight = found["height"] as? Int ?? 0
    guard sourceWidth > 0, sourceHeight > 0 else { fail("the video has no picture") }
    var width = sourceWidth, height = sourceHeight
    if maxWidth > 0 && width > maxWidth {
        width = maxWidth
        height = max(2, Int((Double(sourceHeight) * Double(maxWidth) / Double(sourceWidth) / 2).rounded()) * 2)
    }
    width -= width % 2
    height -= height % 2
    let hasAudio = found["hasAudio"] as? Bool ?? false
    let info: [String: Any] = ["start": start, "hasAudio": hasAudio, "hasVideo": true, "rate": 44100, "channels": 2, "backend": "ffmpeg"]
    packet(UInt8(ascii: "I"), start, (try? JSONSerialization.data(withJSONObject: info)) ?? Data())

    func launch(_ parameters: [String]) -> (Process, FileHandle)? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: ffmpeg)
        process.arguments = parameters
        let output = Pipe()
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        do { try process.run() } catch { return nil }
        return (process, output.fileHandleForReading)
    }
    guard let (pictureProcess, pictures) = launch(["-v", "error", "-ss", number(start), "-i", videoPath, "-an", "-sn",
                                                   "-vf", "fps=30,scale=\(width):\(height)", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"]) else { fail("ffmpeg did not start") }
    let sound = hasAudio ? launch(["-v", "error", "-ss", number(start), "-i", videoPath, "-vn", "-sn", "-ac", "2", "-ar", "44100", "-f", "f32le", "-"]) : nil

    let video = Feed(capacity: 8)
    let audio = Feed(capacity: 48)
    let frameSize = width * height * 3
    let space = CGColorSpaceCreateDeviceRGB()
    Thread.detachNewThread {
        var index = 0
        while true {
            let raw = read(pictures, frameSize)
            if raw.count < frameSize { break }
            guard let provider = CGDataProvider(data: raw as CFData),
                  let image = CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 24, bytesPerRow: width * 3, space: space,
                                      bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.none.rawValue), provider: provider, decode: nil,
                                      shouldInterpolate: false, intent: .defaultIntent) else { break }
            video.put((start + Double(index) / 30.0, jpeg(image, quality: 0.8)))
            index += 1
        }
        video.finish()
    }
    if let (_, samples) = sound {
        Thread.detachNewThread {
            var played = 0
            while true {
                let chunk = read(samples, 4096 * 8)
                if chunk.isEmpty { break }
                audio.put((start + Double(played) / 44100.0, chunk))
                played += chunk.count / 8
            }
            audio.finish()
        }
    } else {
        audio.finish()
    }
    while true {
        let nextVideo = video.peek()
        let nextAudio = audio.peek()
        if nextVideo == nil && nextAudio == nil { break }
        if let picture = nextVideo, nextAudio == nil || picture.0 <= nextAudio!.0 {
            packet(UInt8(ascii: "V"), picture.0, picture.1)
            video.pop()
        } else if let part = nextAudio {
            packet(UInt8(ascii: "A"), part.0, part.1)
            audio.pop()
        }
    }
    pictureProcess.waitUntilExit()
    sound?.0.waitUntilExit()
    packet(UInt8(ascii: "E"), 0, Data())
}

/// What AVFoundation says of the video, or, when it cannot read it, what ffprobe says (with ffmpeg next to the tool).
func probeAny() async -> [String: Any] {
    var found = await probe()
    if found["playable"] as? Bool == true {
        found["backend"] = "avfoundation"
        return found
    }
    return ffProbe() ?? found
}

// ── The commands ──

let found = await probeAny()
let viaFfmpeg = found["backend"] as? String == "ffmpeg"

switch command {
case "probe":
    printJson(found)

case "card":
    var card = found
    let length = found["duration"] as? Double ?? 0
    let width = Int(argument(4, 320))
    let image = viaFfmpeg ? ffPicture(min(length * 0.1, 30), width: width)
        : found["hasVideo"] as? Bool == true ? await picture(generator(width: width, exact: false), at: min(length * 0.1, 30)) : nil
    if let image {
        write(jpeg(image, quality: 0.8), to: text(3))
        card["thumbnail"] = text(3)
    }
    printJson(card)

case "thumb":
    let exact = text(6) == "exact"
    let width = Int(argument(5, 0))
    guard let image = viaFfmpeg ? ffPicture(argument(3), width: width) : await picture(generator(width: width, exact: exact), at: argument(3)) else {
        fail("no picture at \(argument(3)) s")
    }
    write(jpeg(image, quality: 0.9), to: text(4))
    printJson(["width": image.width, "height": image.height])

case "strip":
    let count = max(1, Int(argument(3, 40)))
    let height = max(16, Int(argument(5, 72)))
    let length = found["duration"] as? Double ?? 0
    let times = (0..<count).map { length * (Double($0) + 0.5) / Double(count) }
    var frames: [CGImage] = []
    if viaFfmpeg {
        frames = ffPictures(times, height: height).map { $0.0 }
    } else {
        let pictures = generator(width: height * 3, exact: false)
        for at in times {
            if let image = await picture(pictures, at: at) { frames.append(image) }
        }
    }
    guard let first = frames.first else { fail("no picture in the video") }
    let cell = max(1, Int((Double(height) * Double(first.width) / Double(max(1, first.height))).rounded()))
    let strip = context(cell * frames.count, height)
    for (index, image) in frames.enumerated() {
        strip.draw(image, in: CGRect(x: index * cell, y: 0, width: cell, height: height))
    }
    write(jpeg(strip.makeImage()!, quality: 0.78), to: text(4))
    printJson(["count": frames.count, "cell": cell, "height": height])

case "sheet":
    let start = argument(3), end = max(argument(3) + 0.1, argument(4))
    let count = max(1, min(16, Int(argument(5, 6))))
    let cellWidth = max(80, Int(argument(7, 400)))
    let times = (0..<count).map { index in min(count == 1 ? (start + end) / 2 : start + (end - start) * Double(index) / Double(count - 1), end - 0.05) }
    var frames: [(CGImage, Double)] = []
    if viaFfmpeg {
        frames = ffPictures(times, width: cellWidth * 2)
    } else {
        let pictures = generator(width: cellWidth * 2, exact: true)
        for at in times {
            if let image = await picture(pictures, at: at) { frames.append((image, at)) }
        }
    }
    guard let first = frames.first?.0 else { fail("no picture in the segment") }
    let columns = frames.count <= 2 ? frames.count : frames.count <= 4 ? 2 : frames.count <= 9 ? 3 : 4
    let rows = (frames.count + columns - 1) / columns
    let cellHeight = max(1, Int((Double(cellWidth) * Double(first.height) / Double(max(1, first.width))).rounded()))
    let gap = 6
    let sheet = context(columns * cellWidth + (columns + 1) * gap, rows * cellHeight + (rows + 1) * gap)
    sheet.setFillColor(CGColor(red: 0.06, green: 0.07, blue: 0.09, alpha: 1))
    sheet.fill(CGRect(x: 0, y: 0, width: sheet.width, height: sheet.height))
    for (index, (image, at)) in frames.enumerated() {
        let column = index % columns, row = index / columns
        let x = gap + column * (cellWidth + gap)
        let y = sheet.height - (row + 1) * (cellHeight + gap)
        sheet.draw(image, in: CGRect(x: x, y: y, width: cellWidth, height: cellHeight))
        let size = CGFloat(max(11, cellWidth / 22))
        label(sheet, "\(index + 1) · \(clock(at))", at: CGPoint(x: CGFloat(x) + 6, y: CGFloat(y + cellHeight) - size * 1.55 - 6), size: size)
    }
    write(jpeg(sheet.makeImage()!, quality: 0.82), to: text(6))
    printJson(["count": frames.count, "columns": columns, "rows": rows])

case "audio" where viaFfmpeg:
    guard found["hasAudio"] as? Bool == true else { fail("the video has no sound") }
    let start = argument(3), end = max(argument(3) + 0.1, argument(4))
    guard run(ffmpeg, ["-v", "error", "-ss", number(start), "-t", number(end - start), "-i", videoPath, "-vn", "-ac", "1", "-ar", "16000",
                       "-c:a", "pcm_s16le", "-f", "wav", "-y", text(5)]) != nil else { fail("the sound cannot be read") }
    let size = (try? FileManager.default.attributesOfItem(atPath: text(5))[.size] as? Int) ?? 44
    printJson(["seconds": Double(max(0, size - 44)) / 32000.0])

case "audio":
    let sounds = (try? await asset.loadTracks(withMediaType: .audio)) ?? []
    guard !sounds.isEmpty else { fail("the video has no sound") }
    guard let reader = try? AVAssetReader(asset: asset) else { fail("the video cannot be read") }
    let output = AVAssetReaderAudioMixOutput(audioTracks: sounds, audioSettings: [
        AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: 16000, AVNumberOfChannelsKey: 1,
        AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false, AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false
    ])
    reader.add(output)
    reader.timeRange = CMTimeRange(start: time(argument(3)), end: time(max(argument(3) + 0.1, argument(4))))
    guard reader.startReading() else { fail("the sound cannot be read: \(reader.error?.localizedDescription ?? "?")") }
    var pcm = Data()
    while let buffer = output.copyNextSampleBuffer() {
        pcm.append(bytes(buffer))
    }
    var wav = Data("RIFF".utf8)
    func add<T>(_ value: T) { var copy = value; withUnsafeBytes(of: &copy) { wav.append(contentsOf: $0) } }
    add(UInt32(36 + pcm.count).littleEndian)
    wav.append(Data("WAVEfmt ".utf8))
    add(UInt32(16).littleEndian); add(UInt16(1).littleEndian); add(UInt16(1).littleEndian)
    add(UInt32(16000).littleEndian); add(UInt32(32000).littleEndian); add(UInt16(2).littleEndian); add(UInt16(16).littleEndian)
    wav.append(Data("data".utf8))
    add(UInt32(pcm.count).littleEndian)
    wav.append(pcm)
    write(wav, to: text(5))
    printJson(["seconds": Double(pcm.count) / 32000.0])

case "play":
    if viaFfmpeg {
        ffPlay(from: argument(3), width: Int(argument(4, 1280)), probe: found)
    } else {
        await play(from: argument(3), width: Int(argument(4, 1280)))
    }

default:
    fail("unknown command \(command)")
}
