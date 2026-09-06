# System's Frontend Requirements:
- **Responsive Design**: The frontend must be fully responsive, ensuring optimal user experience across various devices (desktops, tablets, smartphones).
- **Cross-Browser Compatibility**: The application should function correctly on all major browsers, including Chrome, Firefox, Safari, and Edge.
- **User-Friendly Interface**: The design should be intuitive and easy to navigate, with clear calls to action and minimal learning curve for new users.
- **Accessibility**: The frontend must comply with WCAG 2.1 guidelines to ensure accessibility for users with disabilities.
- **Performance Optimization**: The frontend should load quickly and efficiently, minimizing the use of heavy assets and optimizing code for performance.
- **Scalability**: The design should accommodate future growth and additional features without requiring a complete overhaul.
- **Security**: Implement measures to protect against common web vulnerabilities, such as XSS and CSRF attacks.
- **Integration with Backend**: The frontend must seamlessly integrate with the backend services via APIs, ensuring smooth data flow and interactions.
- **State Management**: Use appropriate state management techniques to handle application state effectively, especially for complex applications.
- **Testing**: Implement thorough testing strategies, including unit tests, integration tests, and end-to-end tests to ensure reliability and functionality.
- **Documentation**: Provide comprehensive documentation for the frontend codebase, including setup instructions, code structure, and usage guidelines.
- **Version Control**: Use a version control system (e.g., Git) to manage code changes and collaborate with other developers.
- **Use of Modern Technologies**: Leverage modern frontend frameworks and libraries (e.g., React, Vue, Angular) to build a robust and maintainable application.
- **SEO Optimization**: Ensure the frontend is optimized for search engines, including proper use of meta tags, semantic HTML, and fast loading times.

## Resources
app will come in 2 languages: English and Arabic, so layout should support both LTR and RTL
- [Frontend Best Practices](https://frontendbestpractices.com/)
- [Web Accessibility Guidelines (WCAG) 2.1](https://www.w3.org/WAI/standards-guidelines/wcag/)
- [Responsive Web Design Basics](https://developers.google.com/web/fundamentals/design-and-ux/responsive)
- [Cross-Browser Testing Tools](https://www.browserstack.com/)
- [Performance Optimization Techniques](https://developers.google.com/web/fundamentals/performance)
- [State Management in Frontend Applications](https://redux.js.org/)
- [Frontend Testing Strategies](https://testing-library.com/docs/react-testing-library/intro/)

## Pages
- Login Page
- POS Main Page (already done but needs to be modified to fit new design and be linked to the backend) !!!! Which will not have scroll body, everything should fit in the screen
- Dashboard Page contains charts and stats about sales, products, customers, etc.
- ManagementPage
  - Products Page
  - Categories Page
  - Users Page
  - Customers Page
  - Suppliers Page

## Components for management page
- tab bar component containing a tab for each page
- datatables with server side processing
- modals for adding/editing/deleting items
- forms for adding/editing items
- search bar
- pagination controls
- filters (e.g., by category, by date)
- sort controls (e.g., by name, by price)
- notifications (e.g., success, error) using toastr
- loading spinners
- confirmation dialogs
- Components should be View Components in ASP.NET Core MVC invoking asynchronously

## Components C#
- need to add 2 tables ( Codelist and CodeListValue + their management repos and services ) where a code list could be for example "Payment Methods, Country" and the values could be "Cash", "Credit Card", "Mobile Payment", "Lebanon", "Syria", etc.
- a C# Component which will take as paramenters the codeList Key and the selected value and if multiple selection is allowed or not and a selectList of values that ovverrides the values in the database (for example for categories we want to show only the categories that are active) and return a select2 component with the values from the database or from the selectList if provided
- a C# Component for datatables that will take as parameters the table id, the columns, the ajax url, and any other options needed for datatables and return a datatable with server side processing
- a C# Component for modals that will take as parameters the modal id, the title, the body, and the footer and return a modal
- a C# Component for forms that will take as parameters the form id, the fields, and the submit button and return a form
- a C# Component for search bar that will take as parameters the placeholder text and the search button and return a search bar
- a C# Component for pagination controls that will take as parameters the current page, the total pages, and the page change callback and return pagination controls

## Libraries
- [jQuery](https://jquery.com/)
- [Bootstrap 5](https://getbootstrap.com/)
- [Select2](https://select2.org/)
- [DataTables](https://datatables.net/)
- [Toastr](https://codeseven.github.io/toastr/)
- [FontAwesome](https://fontawesome.com/)
- [Moment.js](https://momentjs.com/)
- [Lodash](https://lodash.com/)
- [Axios](https://axios-http.com/)
- [SweetAlert2](https://sweetalert2.github.io/)
- [Chart.js](https://www.chartjs.org/)
- [Daterangepicker](https://www.daterangepicker.com/)
- [jQuery Validation](https://jqueryvalidation.org/)
- [jQuery Mask Plugin](https://igorescobar.github.io/jQuery-Mask-Plugin/)
- [jQuery UI](https://jqueryui.com/)
- [Popper.js](https://popper.js.org/)
- [Animate.css](https://animate.style/)
- [AOS (Animate On Scroll)](https://michalsnik.github.io/aos/)