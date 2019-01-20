﻿using OpenSage.Gui.Wnd;
using OpenSage.Gui.Wnd.Controls;

namespace OpenSage.Mods.Generals.Gui
{
    [WndCallbacks]
    public static class SkirmishGameOptionsMenuCallbacks
    {
        public static GameOptionsUtil GameOptions { get; private set; }

        public static void SkirmishGameOptionsMenuSystem(Control control, WndWindowMessage message, ControlCallbackContext context)
        {
            if (!GameOptions.HandleSystem(control, message, context))
            {
                switch (message.MessageType)
                {
                    case WndWindowMessageType.SelectedButton:
                        switch (message.Element.Name)
                        {
                            case "SkirmishGameOptionsMenu.wnd:ButtonBack":
                                context.WindowManager.SetWindow(@"Menus\MainMenu.wnd");
                                // TODO: Go back to Single Player sub-menu
                                break;
                        }
                        break;
                }
            }
        }

        public static void SkirmishGameOptionsMenuInit(Window window, Game game)
        {
            GameOptions = new GameOptionsUtil(window, game, "Skirmish");
        }
    }
}
