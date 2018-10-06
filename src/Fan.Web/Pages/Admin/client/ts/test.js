import Vue from 'vue';
import axios from 'axios';
class App extends Vue {
    constructor() {
        super(...arguments);
        this.drawer = null;
        this.snackbar = {
            show: false,
            text: '',
            color: '',
            timeout: 0,
        };
    }
    get tok() {
        return document.querySelector('input[name="__RequestVerificationToken"][type="hidden"]').value;
    }
    get headers() {
        return { headers: { 'XSRF-TOKEN': this.tok } };
    }
    mounted() {
        console.log('inside mounted');
        this.initActiveNav();
    }
    initActiveNav() {
        var url = window.location.pathname;
        this.adminNavs.forEach(function (nav) {
        });
    }
    logout() {
        console.log('logout');
        axios.post('/home/logout', null, this.headers)
            .then(function (response) {
            window.location.href = '/';
        })
            .catch(function (error) {
            console.log(error);
        });
    }
    toast(text, timeout = 3000, color = 'silver') {
        this.snackbar.show = true;
        this.snackbar.text = text;
        this.snackbar.color = color;
        this.snackbar.timeout = timeout;
    }
    toastError(text, timeout = 3000) {
        this.snackbar.show = true;
        this.snackbar.text = text;
        this.snackbar.color = 'red';
        this.snackbar.timeout = timeout;
    }
}
//# sourceMappingURL=test.js.map