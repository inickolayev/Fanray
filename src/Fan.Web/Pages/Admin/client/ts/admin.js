var app = new Vue({
    el: '#app',
    mixins: [adminMixin],
    data: () => ({
        drawer: null,
        snackbar: {
            show: false,
            text: '',
            color: '',
            timeout: 0,
        },
    }),
    computed: {
        tok: function () {
            let t = document.querySelector('input[name="__RequestVerificationToken"][type="hidden"]');
            return t.value;
        },
        headers() {
            return { headers: { 'XSRF-TOKEN': this.tok } };
        },
    },
    mounted() {
        this.initActiveNav();
    },
    methods: {
        initActiveNav() {
            var url = window.location.pathname;
            this.adminNavs.forEach(function (nav) {
                nav.active = url.startsWith(nav.url);
            });
        },
        logout: function () {
            console.log('logout');
            axios.post('/home/logout', null, this.headers)
                .then(function (response) {
                window.location.href = '/';
            })
                .catch(function (error) {
                console.log(error);
            });
        },
        toast(text, timeout = 3000, color = 'silver') {
            this.snackbar.show = true;
            this.snackbar.text = text;
            this.snackbar.color = color;
            this.snackbar.timeout = timeout;
        },
        toastError(text, timeout = 3000) {
            this.snackbar.show = true;
            this.snackbar.text = text;
            this.snackbar.color = 'red';
            this.snackbar.timeout = timeout;
        },
    }
});
//# sourceMappingURL=admin.js.map