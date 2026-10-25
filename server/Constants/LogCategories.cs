/*!************************************************************************
\file   LogCategories.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the names that log lines are filed under, so the
        log can be filtered by area.

        Used by the player auth handlers that log through ILoggerFactory.
**************************************************************************/
namespace server.Constants
{
    // names log lines are filed under, so you can filter by area
    public static class LogCategories
    {
        public const string PlayerAuth = "PlayerAuth";
    }
}
