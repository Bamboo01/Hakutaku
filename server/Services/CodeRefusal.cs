/*!************************************************************************
\file   CodeRefusal.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the CodeRefusal enum, which says why
        EmailCodeService did not make a new code: one was made a moment ago,
        or the account has used up its codes for the day.

        Returned by EmailCodeService.CreateCode. Handled in ResendEmail.cs.
**************************************************************************/
namespace server.Services
{
    // why CreateCode didn't make a code
    public enum CodeRefusal
    {
        None,

        // one was already made in the last ResendSeconds
        TooSoon,

        // the account already had RateLimits.CodesPerDay codes of this kind in the last 24 hours
        TooManyToday,
    }
}
