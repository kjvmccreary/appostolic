## Various frontend cleanup

1. Style the login page. — Done (title, primary button, spacing)
2. Create link for signup on the login page. — Done (links row)
3. When logged out I can still see items on the TopBar: Dashboard, Agents, Shepherd, Editor, Account button. — Done (TopBar now hides nav; shows Sign in)
4. Need to provide link to Magic Link login while on Signin Page. — Done (links row)
5. Make sure Magic Link page is styled. — Done (request/verify pages use shared spacing, headings)
6. No link on members page to invite new members. — Done (added link to /studio/admin/invites)
7. 500 error when attempting to change Role assignment for a user on Members page. — Verified fixed via guards + tests
8. In the TopBar when logged in, the Create Lesson button and the New Agent button are not consistently styled. — Done (unified styles)
9. When clicking Switch Tenant in the user account button (top right), the switcher dialog is at the top of the screen and the top most part is cut off. need to move it down on the Y axis. — Done (modal alignment and max-height)
10. The mobile menu apparently has a transparent background because when in dark mode, the text from the mobile menu is overlaid whatever text from the page is below — Done (backdrop blur + solid surface for panel)
