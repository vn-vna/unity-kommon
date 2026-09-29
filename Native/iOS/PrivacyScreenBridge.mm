#import <UIKit/UIKit.h>

static UIView *_privacyCover;

static UIWindow *FindActiveKeyWindow() {
    UIApplication *application = UIApplication.sharedApplication;
    for (UIScene *scene in application.connectedScenes) {
        if (![scene isKindOfClass:UIWindowScene.class]) continue;

        UIWindowScene *windowScene = (UIWindowScene *)scene;
        for (UIWindow *window in windowScene.windows) {
            if (window.isKeyWindow) return window;
        }
    }
    return nil;
}

static void RunOnMainThread(void (^action)(void)) {
    if (NSThread.isMainThread) {
        action();
    } else {
        dispatch_async(dispatch_get_main_queue(), action);
    }
}

extern "C" {

void scheherazade_privacy_showCover() {
    RunOnMainThread(^{
        if (_privacyCover != nil) return;

        UIWindow *window = FindActiveKeyWindow();
        UIView *rootView = window.rootViewController.view;
        if (rootView == nil) return;

        UIView *cover = [[UIView alloc] initWithFrame:rootView.bounds];
        cover.backgroundColor = UIColor.blackColor;
        cover.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
        cover.userInteractionEnabled = NO;
        [rootView addSubview:cover];
        [rootView bringSubviewToFront:cover];
        _privacyCover = cover;

#if !__has_feature(objc_arc)
        [cover release];
#endif
    });
}

void scheherazade_privacy_removeCover() {
    RunOnMainThread(^{
        [_privacyCover removeFromSuperview];
        _privacyCover = nil;
    });
}

} // extern "C"
