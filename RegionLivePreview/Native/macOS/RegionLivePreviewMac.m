#import <AppKit/AppKit.h>
#import <Carbon/Carbon.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#import <CoreVideo/CoreVideo.h>
#import <CoreMedia/CoreMedia.h>
#import <math.h>
#import <pthread.h>
#import <stdint.h>
#import <stdio.h>
#import <string.h>

typedef struct {
    uint32_t displayId;
    int32_t x;
    int32_t y;
    int32_t width;
    int32_t height;
    int32_t workX;
    int32_t workY;
    int32_t workWidth;
    int32_t workHeight;
    double scaling;
    int32_t isPrimary;
    int32_t windowX;
    int32_t windowY;
} RLPScreenInfo;

typedef void (*RLPHotkeyCallback)(void *context);

static void RLPSetError(char *buffer, size_t capacity, NSString *message)
{
    if (buffer == NULL || capacity == 0) {
        return;
    }

    const char *utf8 = message.UTF8String;
    if (utf8 == NULL) {
        buffer[0] = '\0';
        return;
    }

    size_t length = strlen(utf8);
    if (length >= capacity) {
        length = capacity - 1;
    }

    memcpy(buffer, utf8, length);
    buffer[length] = '\0';
}

int32_t RLPGetScreens(RLPScreenInfo *screens, int32_t capacity)
{
    @autoreleasepool {
        uint32_t displayCount = 0;
        if (CGGetActiveDisplayList(0, NULL, &displayCount) != kCGErrorSuccess || displayCount == 0) {
            return 0;
        }

        if (screens == NULL || capacity == 0) {
            return (int32_t)displayCount;
        }

        displayCount = MIN(displayCount, (uint32_t)capacity);
        CGDirectDisplayID *displayIds = calloc(displayCount, sizeof(CGDirectDisplayID));
        if (displayIds == NULL) {
            return 0;
        }

        if (CGGetActiveDisplayList(displayCount, displayIds, &displayCount) != kCGErrorSuccess) {
            free(displayIds);
            return 0;
        }

        CGDirectDisplayID primaryId = CGMainDisplayID();
        CGFloat primaryScale = 1.0;
        CGDisplayModeRef primaryMode = CGDisplayCopyDisplayMode(primaryId);
        CGRect primaryBounds = CGDisplayBounds(primaryId);
        if (primaryMode != NULL && primaryBounds.size.width > 0) {
            primaryScale = (CGFloat)CGDisplayModeGetPixelWidth(primaryMode) / primaryBounds.size.width;
        }

        NSMutableDictionary<NSNumber *, NSScreen *> *screensById = [NSMutableDictionary dictionary];
        for (NSScreen *screen in [NSScreen screens]) {
            NSNumber *number = screen.deviceDescription[@"NSScreenNumber"];
            if (number != nil) {
                screensById[number] = screen;
            }
        }

        int32_t count = 0;
        for (uint32_t index = 0; index < displayCount; index++) {
            CGDirectDisplayID displayId = displayIds[index];
            CGDisplayModeRef mode = CGDisplayCopyDisplayMode(displayId);
            if (mode == NULL) {
                continue;
            }

            CGRect bounds = CGDisplayBounds(displayId);
            CGFloat scale = bounds.size.width > 0
                ? (CGFloat)CGDisplayModeGetPixelWidth(mode) / bounds.size.width
                : 1.0;
            screens[count].displayId = displayId;
            screens[count].x = (int32_t)llround(bounds.origin.x * primaryScale);
            screens[count].y = (int32_t)llround(bounds.origin.y * primaryScale);
            screens[count].width = MAX(1, (int32_t)llround(bounds.size.width * scale));
            screens[count].height = MAX(1, (int32_t)llround(bounds.size.height * scale));

            NSScreen *screen = screensById[@(displayId)];
            if (screen != nil) {
                NSScreen *primary = screensById[@(primaryId)];
                CGFloat primaryHeight = primary != nil ? primary.frame.size.height : primaryBounds.size.height;
                NSRect visible = screen.visibleFrame;
                CGFloat visibleTop = primaryHeight - visible.origin.y - visible.size.height;
                screens[count].workX = (int32_t)llround(visible.origin.x * primaryScale);
                screens[count].workY = (int32_t)llround(visibleTop * primaryScale);
                screens[count].workWidth = MAX(1, (int32_t)llround(visible.size.width * scale));
                screens[count].workHeight = MAX(1, (int32_t)llround(visible.size.height * scale));
            } else {
                screens[count].workX = screens[count].x;
                screens[count].workY = screens[count].y;
                screens[count].workWidth = screens[count].width;
                screens[count].workHeight = screens[count].height;
            }

            screens[count].scaling = scale;
            screens[count].isPrimary = displayId == primaryId ? 1 : 0;
            screens[count].windowX = (int32_t)llround(bounds.origin.x);
            screens[count].windowY = (int32_t)llround(bounds.origin.y);
            CGDisplayModeRelease(mode);
            count++;
        }

        if (primaryMode != NULL) {
            CGDisplayModeRelease(primaryMode);
        }
        free(displayIds);
        return count;
    }
}

int32_t RLPPreflightScreenCaptureAccess(void)
{
    return CGPreflightScreenCaptureAccess() ? 1 : 0;
}

int32_t RLPRequestScreenCaptureAccess(void)
{
    return CGRequestScreenCaptureAccess() ? 1 : 0;
}

int32_t RLPSetWindowClickThrough(void *windowPointer, int32_t enabled)
{
    if (windowPointer == NULL) {
        return 0;
    }

    @autoreleasepool {
        NSWindow *window = (__bridge NSWindow *)windowPointer;
        window.ignoresMouseEvents = enabled != 0;
        return 1;
    }
}

int32_t RLPGetWindowBounds(void *windowPointer, int32_t *x, int32_t *y, int32_t *width, int32_t *height)
{
    if (windowPointer == NULL) {
        return 0;
    }

    @autoreleasepool {
        NSWindow *window = (__bridge NSWindow *)windowPointer;
        NSRect frame = window.frame;
        NSScreen *primary = [NSScreen screens].firstObject;
        CGFloat primaryHeight = primary.frame.size.height;
        CGFloat primaryScale = primary.backingScaleFactor;

        if (x != NULL) {
            *x = (int32_t)llround(frame.origin.x * primaryScale);
        }
        if (y != NULL) {
            *y = (int32_t)llround((primaryHeight - frame.origin.y - frame.size.height) * primaryScale);
        }
        if (width != NULL) {
            *width = (int32_t)llround(frame.size.width * primaryScale);
        }
        if (height != NULL) {
            *height = (int32_t)llround(frame.size.height * primaryScale);
        }
        return 1;
    }
}

static EventHandlerRef RLPHotkeyHandlerRef = NULL;
static EventHotKeyRef RLPHotkeyRef = NULL;
static RLPHotkeyCallback RLPHotkeyCallbackFunction = NULL;
static void *RLPHotkeyCallbackContext = NULL;
static const OSType RLPHotkeySignature = 'RLPV';

static OSStatus RLPHandleHotkey(EventHandlerCallRef nextHandler, EventRef event, void *userData)
{
    EventHotKeyID hotkeyId;
    OSStatus status = GetEventParameter(
        event,
        kEventParamDirectObject,
        typeEventHotKeyID,
        NULL,
        sizeof(hotkeyId),
        NULL,
        &hotkeyId);

    if (status == noErr
        && hotkeyId.signature == RLPHotkeySignature
        && RLPHotkeyCallbackFunction != NULL) {
        RLPHotkeyCallbackFunction(RLPHotkeyCallbackContext);
        return noErr;
    }

    return eventNotHandledErr;
}

int32_t RLPRegisterHotkey(
    uint32_t keyCode,
    uint32_t modifiers,
    RLPHotkeyCallback callback,
    void *context,
    char *errorBuffer,
    size_t errorCapacity)
{
    if (RLPHotkeyHandlerRef == NULL) {
        EventTypeSpec eventType = {
            .eventClass = kEventClassKeyboard,
            .eventKind = kEventHotKeyPressed
        };

        OSStatus status = InstallApplicationEventHandler(
            NewEventHandlerUPP(RLPHandleHotkey),
            1,
            &eventType,
            NULL,
            &RLPHotkeyHandlerRef);

        if (status != noErr) {
            RLPSetError(errorBuffer, errorCapacity, [NSString stringWithFormat:@"InstallApplicationEventHandler failed: %d", (int)status]);
            return status;
        }
    }

    if (RLPHotkeyRef != NULL) {
        UnregisterEventHotKey(RLPHotkeyRef);
        RLPHotkeyRef = NULL;
    }

    EventHotKeyID hotkeyId = {
        .signature = RLPHotkeySignature,
        .id = 1
    };

    OSStatus status = RegisterEventHotKey(
        keyCode,
        modifiers,
        hotkeyId,
        GetApplicationEventTarget(),
        0,
        &RLPHotkeyRef);

    if (status != noErr) {
        RLPSetError(errorBuffer, errorCapacity, [NSString stringWithFormat:@"RegisterEventHotKey failed: %d", (int)status]);
        RLPHotkeyCallbackFunction = NULL;
        RLPHotkeyCallbackContext = NULL;
        return status;
    }

    RLPHotkeyCallbackFunction = callback;
    RLPHotkeyCallbackContext = context;
    return noErr;
}

void RLPUnregisterHotkey(void)
{
    RLPHotkeyCallbackFunction = NULL;
    RLPHotkeyCallbackContext = NULL;

    if (RLPHotkeyRef != NULL) {
        UnregisterEventHotKey(RLPHotkeyRef);
        RLPHotkeyRef = NULL;
    }
}

@interface RLPSCKCapture : NSObject <SCStreamOutput, SCStreamDelegate>
@property(nonatomic, strong) SCStream *stream;
@property(nonatomic, strong) dispatch_queue_t sampleQueue;
@property(nonatomic, assign) int32_t outputWidth;
@property(nonatomic, assign) int32_t outputHeight;
@end

@implementation RLPSCKCapture {
@public
    pthread_mutex_t _mutex;
    uint8_t *_latestFrame;
    size_t _latestCapacity;
    BOOL _hasNewFrame;
    NSString *_lastError;
}

- (instancetype)initWithWidth:(int32_t)width height:(int32_t)height
{
    self = [super init];
    if (self == nil) {
        return nil;
    }

    pthread_mutex_init(&_mutex, NULL);
    self.outputWidth = width;
    self.outputHeight = height;
    self.sampleQueue = dispatch_queue_create(
        "com.regionlivepreview.screencapture",
        DISPATCH_QUEUE_SERIAL);
    return self;
}

- (void)dealloc
{
    if (_latestFrame != NULL) {
        free(_latestFrame);
        _latestFrame = NULL;
    }
    pthread_mutex_destroy(&_mutex);
}

- (void)storeError:(NSError *)error
{
    pthread_mutex_lock(&_mutex);
    _lastError = error.localizedDescription ?: @"Unknown ScreenCaptureKit error";
    pthread_mutex_unlock(&_mutex);
}

- (void)stream:(SCStream *)stream didOutputSampleBuffer:(CMSampleBufferRef)sampleBuffer ofType:(SCStreamOutputType)type
{
    if (type != SCStreamOutputTypeScreen || !CMSampleBufferIsValid(sampleBuffer)) {
        return;
    }

    CFArrayRef attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, true);
    if (attachments != NULL && CFArrayGetCount(attachments) > 0) {
        CFDictionaryRef attachment = CFArrayGetValueAtIndex(attachments, 0);
        CFTypeRef statusValue = CFDictionaryGetValue(attachment, SCStreamFrameInfoStatus);
        if (statusValue != NULL && CFGetTypeID(statusValue) == CFNumberGetTypeID()) {
            NSInteger status = 0;
            CFNumberGetValue((CFNumberRef)statusValue, kCFNumberNSIntegerType, &status);
            if (status == SCFrameStatusIdle
                || status == SCFrameStatusBlank
                || status == SCFrameStatusSuspended
                || status == SCFrameStatusStopped) {
                return;
            }
        }
    }

    CVPixelBufferRef pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer);
    if (pixelBuffer == NULL) {
        return;
    }

    if (CVPixelBufferLockBaseAddress(pixelBuffer, kCVPixelBufferLock_ReadOnly) != kCVReturnSuccess) {
        return;
    }

    const uint8_t *source = CVPixelBufferGetBaseAddress(pixelBuffer);
    size_t sourceRowBytes = CVPixelBufferGetBytesPerRow(pixelBuffer);
    size_t pixelWidth = CVPixelBufferGetWidth(pixelBuffer);
    size_t pixelHeight = CVPixelBufferGetHeight(pixelBuffer);
    size_t destinationRowBytes = pixelWidth * 4;
    size_t required = destinationRowBytes * pixelHeight;

    pthread_mutex_lock(&_mutex);
    if (required > _latestCapacity) {
        uint8_t *replacement = realloc(_latestFrame, required);
        if (replacement == NULL) {
            _lastError = @"Unable to allocate a screen capture frame buffer";
            pthread_mutex_unlock(&_mutex);
            CVPixelBufferUnlockBaseAddress(pixelBuffer, kCVPixelBufferLock_ReadOnly);
            return;
        }

        _latestFrame = replacement;
        _latestCapacity = required;
    }

    if (source != NULL && _latestFrame != NULL) {
        for (size_t row = 0; row < pixelHeight; row++) {
            memcpy(
                _latestFrame + row * destinationRowBytes,
                source + row * sourceRowBytes,
                destinationRowBytes);
        }
        _hasNewFrame = YES;
        _lastError = nil;
    }
    pthread_mutex_unlock(&_mutex);

    CVPixelBufferUnlockBaseAddress(pixelBuffer, kCVPixelBufferLock_ReadOnly);
}

- (void)stream:(SCStream *)stream didStopWithError:(NSError *)error
{
    [self storeError:error];
}

@end

void *RLPSCKCreate(
    uint32_t displayId,
    double sourceX,
    double sourceY,
    double sourceWidth,
    double sourceHeight,
    int32_t outputWidth,
    int32_t outputHeight,
    int32_t fps,
    char *errorBuffer,
    size_t errorCapacity)
{
    @autoreleasepool {
        dispatch_semaphore_t semaphore = dispatch_semaphore_create(0);
        __block SCShareableContent *shareableContent = nil;
        __block NSError *contentError = nil;

        [SCShareableContent getShareableContentWithCompletionHandler:^(SCShareableContent *content, NSError *error) {
            shareableContent = content;
            contentError = error;
            dispatch_semaphore_signal(semaphore);
        }];

        if (dispatch_semaphore_wait(
                semaphore,
                dispatch_time(DISPATCH_TIME_NOW, (int64_t)(10 * NSEC_PER_SEC))) != 0) {
            RLPSetError(errorBuffer, errorCapacity, @"ScreenCaptureKit timed out while loading shareable content.");
            return NULL;
        }

        if (shareableContent == nil) {
            RLPSetError(errorBuffer, errorCapacity, contentError.localizedDescription ?: @"Unable to access shareable screen content.");
            return NULL;
        }

        SCDisplay *display = nil;
        for (SCDisplay *candidate in shareableContent.displays) {
            if (candidate.displayID == displayId) {
                display = candidate;
                break;
            }
        }

        if (display == nil) {
            RLPSetError(errorBuffer, errorCapacity, @"The selected display is no longer available.");
            return NULL;
        }

        SCContentFilter *filter = [[SCContentFilter alloc]
            initWithDisplay:display
            excludingWindows:@[]];
        if (filter == nil) {
            RLPSetError(errorBuffer, errorCapacity, @"Unable to create a ScreenCaptureKit content filter.");
            return NULL;
        }

        SCStreamConfiguration *configuration = [[SCStreamConfiguration alloc] init];
        configuration.sourceRect = CGRectMake(sourceX, sourceY, sourceWidth, sourceHeight);
        configuration.width = MAX(1, outputWidth);
        configuration.height = MAX(1, outputHeight);
        configuration.pixelFormat = kCVPixelFormatType_32BGRA;
        configuration.minimumFrameInterval = CMTimeMake(1, MAX(1, fps));
        configuration.queueDepth = 3;
        configuration.showsCursor = NO;
        configuration.capturesAudio = NO;
        configuration.preservesAspectRatio = YES;
        configuration.scalesToFit = YES;
        configuration.captureResolution = SCCaptureResolutionBest;

        RLPSCKCapture *capture = [[RLPSCKCapture alloc] initWithWidth:outputWidth height:outputHeight];
        if (capture == nil) {
            RLPSetError(errorBuffer, errorCapacity, @"Unable to allocate the ScreenCaptureKit capture session.");
            return NULL;
        }

        NSError *streamError = nil;
        capture.stream = [[SCStream alloc]
            initWithFilter:filter
            configuration:configuration
            delegate:capture];
        if (capture.stream == nil) {
            RLPSetError(errorBuffer, errorCapacity, streamError.localizedDescription ?: @"Unable to create a ScreenCaptureKit stream.");
            return NULL;
        }

        return (__bridge_retained void *)capture;
    }
}

int32_t RLPSCKStart(void *context, char *errorBuffer, size_t errorCapacity)
{
    if (context == NULL) {
        RLPSetError(errorBuffer, errorCapacity, @"The ScreenCaptureKit capture session is invalid.");
        return -1;
    }

    @autoreleasepool {
        RLPSCKCapture *capture = (__bridge RLPSCKCapture *)context;
        NSError *outputError = nil;
        if (![capture.stream
                addStreamOutput:capture
                type:SCStreamOutputTypeScreen
                sampleHandlerQueue:capture.sampleQueue
                error:&outputError]) {
            RLPSetError(errorBuffer, errorCapacity, outputError.localizedDescription ?: @"Unable to attach a ScreenCaptureKit frame output.");
            return -1;
        }

        dispatch_semaphore_t semaphore = dispatch_semaphore_create(0);
        __block NSError *startError = nil;
        [capture.stream startCaptureWithCompletionHandler:^(NSError *error) {
            startError = error;
            dispatch_semaphore_signal(semaphore);
        }];

        if (dispatch_semaphore_wait(
                semaphore,
                dispatch_time(DISPATCH_TIME_NOW, (int64_t)(10 * NSEC_PER_SEC))) != 0) {
            RLPSetError(errorBuffer, errorCapacity, @"ScreenCaptureKit timed out while starting capture.");
            return -1;
        }

        if (startError != nil) {
            RLPSetError(errorBuffer, errorCapacity, startError.localizedDescription);
            return -1;
        }

        return 0;
    }
}

int32_t RLPSCKCopyLatestFrame(
    void *context,
    uint8_t *destination,
    int32_t destinationCapacity,
    int32_t *width,
    int32_t *height,
    char *errorBuffer,
    size_t errorCapacity)
{
    if (context == NULL || destination == NULL || destinationCapacity <= 0) {
        RLPSetError(errorBuffer, errorCapacity, @"The ScreenCaptureKit frame destination is invalid.");
        return -1;
    }

    RLPSCKCapture *capture = (__bridge RLPSCKCapture *)context;
    pthread_mutex_lock(&capture->_mutex);

    if (capture->_lastError != nil) {
        RLPSetError(errorBuffer, errorCapacity, capture->_lastError);
        pthread_mutex_unlock(&capture->_mutex);
        return -1;
    }

    if (!capture->_hasNewFrame || capture->_latestFrame == NULL) {
        pthread_mutex_unlock(&capture->_mutex);
        return 0;
    }

    size_t required = (size_t)capture.outputWidth * (size_t)capture.outputHeight * 4;
    if (required > (size_t)destinationCapacity) {
        RLPSetError(errorBuffer, errorCapacity, @"The ScreenCaptureKit frame is larger than the allocated buffer.");
        pthread_mutex_unlock(&capture->_mutex);
        return -1;
    }

    memcpy(destination, capture->_latestFrame, required);
    capture->_hasNewFrame = NO;
    if (width != NULL) {
        *width = capture.outputWidth;
    }
    if (height != NULL) {
        *height = capture.outputHeight;
    }
    pthread_mutex_unlock(&capture->_mutex);
    return 1;
}

void RLPSCKDestroy(void *context)
{
    if (context == NULL) {
        return;
    }

    @autoreleasepool {
        RLPSCKCapture *capture = (__bridge_transfer RLPSCKCapture *)context;
        [capture.stream stopCaptureWithCompletionHandler:nil];
        capture.stream = nil;
    }
}
