import { GlobalStyles } from '@mui/material'

// Keep the viewport width unchanged when MUI locks scrolling for a menu,
// dialog or temporary drawer. The gutter replaces MUI's padding compensation.
export function StableViewport() {
  return <GlobalStyles styles={{
    '@supports (scrollbar-gutter: stable)': {
      html: { scrollbarGutter: 'stable' },
      // Customer views use natural page scrolling; short profiles need no blank gutter.
      'html:has(.customer-site-density)': { scrollbarGutter: 'auto' },
      body: { paddingRight: '0 !important' },
      '.mui-fixed': { paddingRight: '0 !important' },
    },
  }} />
}
